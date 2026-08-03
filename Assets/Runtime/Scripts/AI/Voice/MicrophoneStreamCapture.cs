using System;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Drives one microphone for a streaming STT client: device resolution, the looping
    /// <see cref="Microphone"/> ring buffer with wrap-aware reads, the envelope-followed input level,
    /// the Discord-style adaptive voice gate, and conversion to the 16 kHz mono PCM every provider
    /// here expects.
    ///
    /// Deliberately a plain C# class, not a MonoBehaviour: it is owned by the STT client component
    /// (<see cref="ElevenLabsScribeClient"/>, <see cref="MunsitSTTClient"/>) rather than being a
    /// second component designers have to wire up.
    ///
    /// It knows nothing about sockets, sessions or commits — <see cref="Poll"/> reports what the mic
    /// heard and whether it passed the gate; the owning client decides whether to send it and when an
    /// utterance is over. Tuning is pushed in via the properties before <see cref="Start"/>.
    /// </summary>
    public sealed class MicrophoneStreamCapture
    {
        // --- Tuning (pushed by the owning client before Start) ---------------------

        /// <summary>Preferred device name; empty = system default (first connected device).</summary>
        public string Device { get; set; } = string.Empty;

        /// <summary>
        /// Discord-style automatic input sensitivity: derive the gate from a continuously-tracked
        /// ambient noise floor (self-calibrating per mic/room) instead of <see cref="ManualThreshold"/>.
        /// </summary>
        public bool AutoAdjustSensitivity { get; set; } = true;

        /// <summary>Head-room the auto gate keeps above the tracked noise floor (gate ≈ floor × this).</summary>
        public float SensitivityMargin { get; set; } = 2.5f;

        /// <summary>Fixed peak amplitude (0-1) gate used when <see cref="AutoAdjustSensitivity"/> is off.</summary>
        public float ManualThreshold { get; set; } = 0.02f;

        /// <summary>
        /// True = drop the idle audio BEFORE the first voiced chunk of an utterance (leading dead air).
        /// Once speech has started the stream is contiguous, so the natural sub-threshold dips between
        /// syllables are kept. False = pass everything through (push-to-talk).
        /// </summary>
        public bool DropLeadingSilence { get; set; } = true;

        /// <summary>Prefix for this capture's warnings, so they name the owning client.</summary>
        public string LogPrefix { get; set; } = "[Mic]";

        // --- Live state ------------------------------------------------------------

        /// <summary>True between a successful <see cref="Start"/> and <see cref="Stop"/>.</summary>
        public bool IsRecording => _micClip != null;

        /// <summary>The device actually opened (may differ from <see cref="Device"/> if it was absent).</summary>
        public string ResolvedDevice => _micDevice;

        /// <summary>The rate the device actually opened at (may not be 16 kHz — hence the resampling).</summary>
        public int CaptureSampleRate => _captureSampleRate;

        /// <summary>Envelope-followed peak amplitude (0-1) of the most recent chunk; 0 when idle.</summary>
        public float CurrentInputLevel { get; private set; }

        /// <summary>The tracked ambient noise floor (0-1, peak domain); 0 until calibrated.</summary>
        public float CurrentNoiseFloor => _noiseFloor;

        /// <summary>The effective gate the level is compared against right now (adaptive or manual).</summary>
        public float CurrentGateThreshold => AutoAdjustSensitivity ? _openGate : ManualThreshold;

        /// <summary>True once a voiced chunk has been seen in the current utterance.</summary>
        public bool SpeechStarted { get; private set; }

        /// <summary>True if the most recently processed chunk was above the gate.</summary>
        public bool LastChunkVoiced { get; private set; }

        /// <summary>Time.unscaledTime of the most recent voiced chunk — drives the silence timers.</summary>
        public float LastVoiceTime { get; private set; }

        /// <summary>True on the <see cref="Poll"/> that first detected speech in this utterance.</summary>
        public bool SpeechStartedThisPoll { get; private set; }

        // --- Microphone state ------------------------------------------------------
        private string _micDevice;
        private AudioClip _micClip;
        private float[] _readBuffer;
        private int _clipSamples;
        private int _lastReadPos;
        private int _captureSampleRate;

        // --- Adaptive input sensitivity (peak domain) ------------------------------
        private float _smoothedLevel;         // envelope-followed level (instant attack, slow release)
        private float _noiseFloor;            // tracked ambient baseline; the auto gate rides above it
        private float _openGate;              // adaptive threshold to OPEN the gate (start capturing)
        private float _closeGate;             // lower threshold to keep it open (hysteresis, anti-chatter)
        private bool _noiseFloorInitialized;  // false until the first chunk seeds the floor

        /// <summary>
        /// Opens the microphone into a 1-second looping ring buffer. Returns false with a
        /// human-readable <paramref name="error"/> if no device is connected or capture failed.
        /// </summary>
        public bool Start(out string error)
        {
            error = null;

            _micDevice = ResolveDevice();
            if (string.IsNullOrEmpty(_micDevice))
            {
                error = "No microphone device found.";
                return false;
            }

            _micClip = Microphone.Start(_micDevice, true, 1, AudioCodec.TargetSampleRate);
            if (_micClip == null)
            {
                error = "Failed to start microphone capture.";
                return false;
            }

            _clipSamples = _micClip.samples;
            _readBuffer = new float[_clipSamples];
            _lastReadPos = 0;
            _captureSampleRate = _micClip.frequency;

            if (_captureSampleRate != AudioCodec.TargetSampleRate)
            {
                // The device forced its native rate (a Quest opens at 48 kHz). Poll resamples every
                // chunk to 16 kHz before handing it over, so this is informational, not fatal.
                Debug.Log($"{LogPrefix} Mic opened at {_captureSampleRate} Hz; resampling to " +
                          $"{AudioCodec.TargetSampleRate} Hz before streaming.");
            }
            return true;
        }

        /// <summary>Stops the microphone. Safe to call when not recording.</summary>
        public void Stop()
        {
            if (!string.IsNullOrEmpty(_micDevice) && Microphone.IsRecording(_micDevice))
                Microphone.End(_micDevice);
            _micClip = null;
            CurrentInputLevel = 0f;
        }

        /// <summary>
        /// Discards the tracked noise floor and envelope so the gate re-calibrates from scratch.
        /// Call on each mic enable — a different mic or room has a different ambient level.
        /// </summary>
        public void ResetCalibration()
        {
            _smoothedLevel = 0f;
            _noiseFloor = 0f;
            _openGate = 0f;
            _closeGate = 0f;
            _noiseFloorInitialized = false;
            CurrentInputLevel = 0f;
            ClearSpeechState();
        }

        /// <summary>
        /// Re-arms the leading-silence gate for a new utterance (speech has to be detected again).
        /// Call after an utterance is finalised, or the gate stays open and streams ambient noise.
        /// </summary>
        public void ClearSpeechState()
        {
            SpeechStarted = false;
            LastChunkVoiced = false;
            SpeechStartedThisPoll = false;
            LastVoiceTime = Time.unscaledTime;
        }

        /// <summary>
        /// Moves the read head to the mic's current write position, discarding whatever accumulated
        /// while we were not consuming (e.g. during the WebSocket connect handshake).
        /// </summary>
        public void ResyncReadHead()
        {
            if (_micClip == null) return;
            _lastReadPos = Microphone.GetPosition(_micDevice);
        }

        /// <summary>
        /// Reads whatever the mic has written since the last call, updates the input level and voice
        /// gate, and converts the chunk to 16 kHz mono PCM.
        ///
        /// Returns true when a chunk was PROCESSED (so the caller's silence timers are meaningful this
        /// frame); false when the mic is stopped or produced no new samples — in which case all state
        /// is untouched. <paramref name="pcm16"/> is non-null only when the chunk passed the gate and
        /// should be streamed; <paramref name="samples16k"/> is the same audio as floats.
        /// </summary>
        public bool Poll(out float[] samples16k, out byte[] pcm16)
        {
            samples16k = null;
            pcm16 = null;
            SpeechStartedThisPoll = false;

            if (_micClip == null) return false;

            int currentPos = Microphone.GetPosition(_micDevice);
            if (currentPos < 0 || currentPos == _lastReadPos) return false;

            _micClip.GetData(_readBuffer, 0);

            // Collect samples written since the last read, handling ring-buffer wrap.
            int newCount = currentPos >= _lastReadPos
                ? currentPos - _lastReadPos
                : (_clipSamples - _lastReadPos) + currentPos;

            var chunk = new float[newCount];
            float peak = 0f;
            for (int i = 0; i < newCount; i++)
            {
                float sample = _readBuffer[(_lastReadPos + i) % _clipSamples];
                chunk[i] = sample;
                float abs = sample < 0f ? -sample : sample;
                if (abs > peak) peak = abs;
            }
            _lastReadPos = currentPos;

            // Envelope-follow the peak: snap up instantly, decay slowly — a steady level (and meter)
            // instead of frame-to-frame jitter. Still peak domain, so the manual gate stays comparable.
            _smoothedLevel = peak >= _smoothedLevel ? peak : Mathf.Lerp(_smoothedLevel, peak, 0.15f);
            CurrentInputLevel = _smoothedLevel;

            // Effective voice gate. AUTO: derive it from the tracked ambient noise floor so it
            // self-calibrates per mic/room. MANUAL: the fixed threshold. Hysteresis (open > close)
            // stops the gate chattering on the quiet dips between words.
            float openGate, closeGate;
            if (AutoAdjustSensitivity)
            {
                if (!_noiseFloorInitialized) { _noiseFloor = _smoothedLevel; _noiseFloorInitialized = true; }
                _openGate = Mathf.Clamp(_noiseFloor * SensitivityMargin + 0.006f, 0.008f, 0.2f);
                _closeGate = _openGate * 0.6f;
                openGate = _openGate;
                closeGate = _closeGate;
            }
            else
            {
                openGate = closeGate = ManualThreshold;
            }

            // Once speaking, hold the gate open down to the lower close-gate; before speech, require
            // the open-gate.
            bool voiced = SpeechStarted ? _smoothedLevel >= closeGate : _smoothedLevel >= openGate;

            // Track the noise floor ONLY while the gate is closed (silence), so speech bursts don't
            // inflate it: rise slowly toward a creeping ambient level, fall quickly when it gets quieter.
            if (AutoAdjustSensitivity && !voiced)
            {
                float rate = _smoothedLevel > _noiseFloor ? 0.02f : 0.2f;
                _noiseFloor = Mathf.Lerp(_noiseFloor, _smoothedLevel, rate);
            }

            // Only the idle audio BEFORE speech starts is dropped; once the utterance is under way it
            // streams contiguously so it isn't fragmented at every inter-word dip.
            bool passesGate = !DropLeadingSilence || SpeechStarted || voiced;
            if (passesGate)
            {
                samples16k = AudioCodec.ResampleTo16k(chunk, _captureSampleRate > 0
                    ? _captureSampleRate
                    : AudioCodec.TargetSampleRate);
                pcm16 = AudioCodec.FloatsToPcm16(samples16k);
            }

            LastChunkVoiced = voiced;
            if (voiced)
            {
                SpeechStartedThisPoll = !SpeechStarted;
                SpeechStarted = true;
                LastVoiceTime = Time.unscaledTime;
            }
            return true;
        }

        // Picks the configured device if it's currently connected, otherwise the first available one.
        private string ResolveDevice()
        {
            string[] devices = Microphone.devices;
            if (devices == null || devices.Length == 0) return null;

            if (!string.IsNullOrEmpty(Device) && Array.IndexOf(devices, Device) >= 0)
                return Device;

            if (!string.IsNullOrEmpty(Device))
                Debug.LogWarning($"{LogPrefix} Microphone '{Device}' not connected; using '{devices[0]}'.");
            return devices[0];
        }
    }
}
