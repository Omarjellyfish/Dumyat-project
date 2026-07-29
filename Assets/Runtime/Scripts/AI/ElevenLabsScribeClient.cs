using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Real-time speech-to-text using ElevenLabs Scribe v2 Realtime over a WebSocket.
    ///
    /// SESSION vs UTTERANCE: the WebSocket is a persistent SESSION — it is opened once (on the
    /// first mic enable, or explicitly via <see cref="OpenSession"/> when e.g. the player enters
    /// the market) and KEPT OPEN across many utterances. It does NOT close when a transcript is
    /// returned. Close it explicitly with <see cref="CloseSession"/> (e.g. leaving the market).
    ///
    /// The MICROPHONE is a separate toggle (<see cref="SetMicrophoneEnabled"/> /
    /// <see cref="MicrophoneEnabled"/>). Enabling it captures + streams one utterance; disabling it
    /// commits the utterance for transcription. The session stays open the whole time.
    ///
    /// GATING: capture only ever starts when <see cref="ConsentManager.IsVoiceModeAvailable"/> is
    /// true, and the whole session is torn down if consent is revoked mid-session. The negotiation
    /// system should subscribe to <see cref="OnTranscriptionFailed"/> and fall back to scripted lines.
    ///
    /// ENDPOINT/FORMAT: verified against ElevenLabs docs (July 2026) —
    /// https://elevenlabs.io/docs/api-reference/speech-to-text/v-1-speech-to-text-realtime
    ///
    /// TRANSPORT: System.Net.WebSockets.ClientWebSocket (built into .NET — no extra package).
    /// Works in the Editor, on Mono/IL2CPP Standalone and Android. It does NOT work on WebGL.
    /// </summary>
    public class ElevenLabsScribeClient : MonoBehaviour, ISTTModel, IVoiceCaptureSource
    {
        // Injected consent + STT services and event bus (replace the former manager singletons). The
        // STT service supplies the provider-agnostic language code when opening a session.
        private IConsentService _consent;
        private ISTTService _stt;
        private IEventService _events;

        [Inject]
        private void Inject(IConsentService consent, ISTTService stt, IEventService events)
        {
            _consent = consent;
            _stt = stt;
            _events = events;
        }

        // --- ElevenLabs Scribe v2 Realtime protocol constants ----------------------
        private const string Endpoint = "wss://api.elevenlabs.io/v1/speech-to-text/realtime";
        private const int RequiredSampleRate = 16000;   // matches audio_format=pcm_16000

        // Server message_type values we react to.
        private const string MsgPartialTranscript = "partial_transcript";
        private const string MsgCommittedTranscript = "committed_transcript";
        private const string MsgCommittedTranscriptTimestamps = "committed_transcript_with_timestamps";
        private const string MsgSessionStarted = "session_started";

        private const string DefaultConfigResourcePath = "AI/Secrets/ElevenLabsConfig";

        [Header("Configuration")]
        [Tooltip("ElevenLabs credentials. If empty it is loaded from Resources/AI/Secrets/ElevenLabsConfig.")]
        [SerializeField] private ElevenLabsConfig _config;

        [Header("Capture Mode")]
        [Tooltip("If true, automatically enables the microphone whenever voice mode becomes available " +
                 "(otherwise call SetMicrophoneEnabled() — push-to-talk / manual).")]
        [SerializeField] private bool _autoStart = false;

        [Header("Silence Detection")]
        [Tooltip("Seconds of continuous silence after speech before the utterance is finalized.")]
        [SerializeField, Min(0.1f)] private float _silenceThresholdSeconds = 1.2f;
        [Tooltip("Peak sample amplitude (0-1) below which audio is considered silence. Only used when " +
                 "auto-sensitivity is OFF (the manual gate).")]
        [SerializeField, Range(0f, 0.5f)] private float _silenceAmplitudeThreshold = 0.02f;
        // Discord-style automatic input sensitivity (adaptive noise-floor gate). Deliberately NOT serialized:
        // the VoiceCaptureController owns this toggle and pushes it here each utterance (ConfigureSourceForMode),
        // so there is exactly one Inspector control. Defaults true so the client still self-calibrates if used
        // standalone with no controller driving it.
        private bool _autoAdjustSensitivity = true;
        [Tooltip("Head-room the auto gate keeps above the tracked noise floor (gate ≈ floor × this). Higher " +
                 "= less sensitive (rejects more noise), lower = more sensitive. Advanced tuning.")]
        [SerializeField, Range(1.5f, 6f)] private float _autoSensitivityMargin = 2.5f;

        [Header("Reliability")]
        [Tooltip("Seconds to wait for the WebSocket to connect before failing.")]
        [SerializeField, Min(1f)] private float _connectionTimeoutSeconds = 5f;
        [Tooltip("Seconds to wait for the final transcript after committing before failing (session stays open).")]
        [SerializeField, Min(1f)] private float _responseTimeoutSeconds = 5f;

        [Header("Transcript Settling")]
        [Tooltip("Quiet period: after a committed transcript arrives, wait this long for the server to send " +
                 "a REFINED version of the same utterance (Scribe re-emits the segment as it improves, e.g. " +
                 "as committed_transcript_with_timestamps). Each refinement restarts this window, so it is " +
                 "the gap BETWEEN refinements that must fit here — not the total. Too small and the last, " +
                 "best revision is cut off. 0 = fire the first transcript immediately (old behaviour).")]
        [SerializeField, Min(0f)] private float _transcriptSettleSeconds = 1f;

        [Tooltip("Hard cap on the total settle wait, measured from the FIRST committed transcript, so a " +
                 "server that keeps dribbling refinements can never stall the negotiation turn. When a " +
                 "transcript is emitted because it hit this cap, a warning is logged — that means refinements " +
                 "were still arriving and this value is too low.")]
        [SerializeField, Min(0.1f)] private float _transcriptSettleMaxSeconds = 3f;

        [Header("Debug")]
        [Tooltip("Log the full session/utterance lifecycle to the Console for testing.")]
        [SerializeField] private bool _verboseLogging = false;

        [Tooltip("Write each committed utterance's audio (exactly the PCM streamed to the STT server, " +
                 "after silence-gating) to a .wav file so mis-transcriptions can be reviewed. Saved under " +
                 "Application.persistentDataPath/<folder>.")]
        [SerializeField] private bool _dumpSttAudio = true;

        [Tooltip("Sub-folder (under Application.persistentDataPath) for the dumped STT .wav files.")]
        [SerializeField] private string _sttAudioFolderName = "STT_Audio";

        private const string LogPrefix = "[Scribe STT]";

        /// <summary>
        /// Fires once per utterance with the final transcribed text, after the server has stopped
        /// refining it (see the Transcript Settling fields). Session stays open after.
        /// </summary>
        public event Action<string> OnTranscriptionComplete;

        /// <summary>
        /// Fires repeatedly mid-utterance with the latest partial (not-yet-committed) transcript, for a
        /// live caption. Display only — never feed it to the LLM / accept-decline scoring.
        /// </summary>
        public event Action<string> OnPartialTranscript;

        /// <summary>
        /// Fires with a human-readable reason when transcription cannot be completed
        /// (no consent/internet, connect failure, timeout, server error).
        /// </summary>
        public event Action<string> OnTranscriptionFailed;

        /// <summary>True while the microphone is enabled (capturing + streaming an utterance).</summary>
        public bool MicrophoneEnabled { get; private set; }

        /// <inheritdoc/>
        /// <summary>ISTTModel view of capture state — true while the mic is enabled.</summary>
        public bool IsListening => MicrophoneEnabled;

        /// <summary>True while the persistent WebSocket session is open (or connecting).</summary>
        public bool IsSessionOpen => _socketOpen || _connecting;

        /// <summary>
        /// Peak amplitude (0-1) of the most recent captured audio chunk. Useful for a live
        /// input-level meter when calibrating <see cref="_silenceAmplitudeThreshold"/>. 0 when idle.
        /// </summary>
        public float CurrentInputLevel { get; private set; }

        /// <summary>Names of the microphone devices currently connected to the system (live).</summary>
        public static string[] AvailableMicrophoneDevices => Microphone.devices;

        /// <summary>
        /// Preferred microphone device name; empty = system default (first connected device). Applied
        /// on the next capture — toggle the mic off/on to switch devices mid-session.
        /// </summary>
        public string MicrophoneDevice
        {
            get => _microphoneDevice;
            set => _microphoneDevice = value ?? string.Empty;
        }

        /// <summary>
        /// When true (default) the utterance finalizes automatically after
        /// <see cref="_silenceThresholdSeconds"/> of silence following speech. Set false for
        /// push-to-talk, where the caller disables the mic explicitly.
        /// </summary>
        public bool AutoStopOnSilence { get; set; } = true;

        /// <summary>Seconds of silence (after speech) before a voice-activity utterance auto-finalizes.</summary>
        public float SilenceThresholdSeconds
        {
            get => _silenceThresholdSeconds;
            set => _silenceThresholdSeconds = Mathf.Max(0.1f, value);
        }

        /// <summary>Peak amplitude (0-1) below which audio counts as silence for voice-activity detection.</summary>
        public float SilenceAmplitudeThreshold
        {
            get => _silenceAmplitudeThreshold;
            set => _silenceAmplitudeThreshold = Mathf.Clamp(value, 0f, 0.5f);
        }

        /// <summary>
        /// Discord-style automatic input sensitivity. When true the voice gate is derived from a
        /// continuously-tracked ambient noise floor (self-calibrating per mic/room) instead of the fixed
        /// <see cref="SilenceAmplitudeThreshold"/>. Only applies in voice-activity mode.
        /// </summary>
        public bool AutoAdjustSensitivity
        {
            get => _autoAdjustSensitivity;
            set => _autoAdjustSensitivity = value;
        }

        /// <summary>The tracked ambient noise floor (0-1, peak domain). 0 when idle / not calibrated.</summary>
        public float CurrentNoiseFloor => _noiseFloor;

        /// <summary>
        /// The effective voice gate the level is currently compared against: the adaptive open-gate when
        /// auto-sensitivity is on, otherwise the fixed <see cref="SilenceAmplitudeThreshold"/>.
        /// </summary>
        public float CurrentGateThreshold => _autoAdjustSensitivity ? _openGate : _silenceAmplitudeThreshold;

        // The Scribe server rejects a commit with less than 0.3s of uncommitted audio. Require a bit
        // more so a too-short blip is discarded locally instead of triggering a server error.
        private const float MinCommitSeconds = 0.4f;

        [Header("Model Pool")]
        [Tooltip("Selection priority within STTManager; higher = preferred.")]
        [SerializeField] private int _priority = 0;

        /// <inheritdoc/>
        public string ModelName => "ElevenLabs Scribe v2";

        /// <inheritdoc/>
        public bool IsAvailable => _config != null && !string.IsNullOrWhiteSpace(_config.ApiKey);

        /// <inheritdoc/>
        public int Priority => _priority;

        // --- Threading: ClientWebSocket runs off the main thread; Unity APIs and events
        //     must run on it. Cross-thread results are queued and drained in Update(). ---
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private readonly ConcurrentQueue<string> _outgoing = new ConcurrentQueue<string>();

        private CancellationTokenSource _cts;
        private volatile bool _socketOpen;
        private volatile bool _connecting;

        // Each connection attempt gets a unique epoch. Cross-thread callbacks carry the epoch they
        // were started under and are ignored unless it still matches _sessionEpoch — this stops a
        // dead connection's late failure from tearing down a freshly reconnected session.
        private int _sessionEpoch;

        // --- Microphone / utterance state (main-thread only) ---
        // Preferred device, set via the MicrophoneDevice property by the VoiceCaptureController
        // (which now owns the Inspector field). Empty = system default.
        private string _microphoneDevice = string.Empty;
        private string _micDevice;
        private AudioClip _micClip;
        private float[] _readBuffer;
        private int _clipSamples;
        private int _lastReadPos;
        private bool _speechStarted;
        private float _lastVoiceTime;
        private int _captureSampleRate;
        private int _streamedSampleCount;   // samples streamed to the server for the current utterance

        // --- Adaptive input sensitivity (Discord-style noise-floor tracking, peak domain) ---
        private float _smoothedLevel;         // envelope-followed input level (instant attack, slow release)
        private float _noiseFloor;            // tracked ambient baseline; the auto gate rides just above it
        private float _openGate;              // adaptive threshold to OPEN the gate (start capturing)
        private float _closeGate;             // lower threshold to keep it open (hysteresis, anti-chatter)
        private bool _noiseFloorInitialized;  // false until the first chunk of a fresh session seeds the floor

        // --- Per-utterance finalization / timeout state (main-thread only) ---
        private bool _awaitingFinal;
        private float _commitSentTime;

        // --- Transcript settling (main-thread only) ---
        // Scribe does not always send its BEST transcript first: the initial committed_transcript can be
        // a fast pass that the server then re-emits, refined, for the same committed audio (typically as
        // committed_transcript_with_timestamps). Firing the first one we see throws that improvement away.
        // So the first committed transcript only ARMS a short settle window: every later transcript for
        // the same utterance supersedes it and restarts the window, and we emit the last (best) one when
        // the window expires or the hard cap is hit.
        private bool _settling;
        private string _settledTranscript;
        private float _settleDeadline;   // unscaled time at which the current best is emitted
        private float _settleHardStop;   // unscaled time past which no further refinement is awaited
        private float _settleStartTime;  // unscaled time the FIRST transcript for this utterance arrived
        private int _settleRevisions;    // how many refinements superseded the first transcript

        // Accumulates exactly the 16-bit PCM streamed to the server for the current utterance (only the
        // gated audio that actually gets sent), so a committed utterance can be written to a .wav for
        // debugging what the STT engine actually heard. Reset when the mic is (re)enabled.
        private readonly List<byte> _utterancePcm = new List<byte>();

        private void Awake()
        {
            if (_config == null)
                _config = Resources.Load<ElevenLabsConfig>(DefaultConfigResourcePath);

            if (_config == null || string.IsNullOrWhiteSpace(_config.ApiKey))
                Debug.LogWarning("ElevenLabsScribeClient: No ElevenLabsConfig/API key. Listening will fail.");
        }

        private void OnEnable()
        {
            _events?.Subscribe<ConsentChangedEvent>(HandleConsentChanged);
        }

        private void Start()
        {
            if (_autoStart) SetMicrophoneEnabled(true);
        }

        private void OnDisable()
        {
            _events?.Unsubscribe<ConsentChangedEvent>(HandleConsentChanged);
        }

        private void OnDestroy()
        {
            TeardownSocket();
        }

        // --- Public session control ------------------------------------------------

        /// <summary>
        /// Opens the persistent WebSocket session and keeps it open. Idempotent. Call this when the
        /// player enters the market (or just enable the mic — it auto-opens). Fails fast (via
        /// <see cref="OnTranscriptionFailed"/>) if voice mode is unavailable, offline, or unconfigured.
        /// </summary>
        public bool OpenSession()
        {
            if (_socketOpen || _connecting) return true;

            if (_consent == null || !_consent.IsVoiceModeAvailable())
            {
                NotifyFailed("Voice mode unavailable (consent not granted or offline).");
                return false;
            }
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                NotifyFailed("No internet connection.");
                return false;
            }
            if (_config == null || string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                NotifyFailed("ElevenLabs API key is not configured.");
                return false;
            }

            _connecting = true;
            _awaitingFinal = false;
            _cts = new CancellationTokenSource();
            int epoch = ++_sessionEpoch;
            _ = RunSocketAsync(epoch, _cts.Token);   // fire-and-forget; all exceptions handled inside
            Log("Opening session — connecting WebSocket…");
            return true;
        }

        /// <summary>
        /// Closes the persistent session: stops the mic (without committing) and tears down the
        /// socket. Call this when the player leaves the market. Idempotent.
        /// </summary>
        public void CloseSession()
        {
            if (!_socketOpen && !_connecting && !MicrophoneEnabled) return;
            Log("Closing session — tearing down mic + socket.");
            TeardownSocket();
        }

        // --- Public microphone toggle ----------------------------------------------

        /// <summary>
        /// Toggles the microphone. Enabling auto-opens the session (if needed) and begins capturing
        /// one utterance; disabling commits the utterance for transcription and LEAVES THE SESSION
        /// OPEN for the next utterance.
        /// </summary>
        public void SetMicrophoneEnabled(bool enabled)
        {
            if (enabled) EnableMicrophone();
            else DisableMicrophone();
        }

        private void EnableMicrophone()
        {
            if (MicrophoneEnabled) return;

            // Android needs the RECORD_AUDIO runtime grant before Microphone.devices is populated —
            // without it the device check below fails and capture never starts. The consent flow secures
            // it up front for the negotiation path, but a caller that bypasses consent (e.g. the STT test
            // harness) can reach here without it. Request it and bail this attempt: the async grant can't
            // retroactively fill this call, so the next mic-enable proceeds once it is allowed.
            if (!MicrophonePermission.IsGranted)
            {
                MicrophonePermission.Request(_ => { });
                NotifyFailed("Microphone permission not granted yet — requested it; try again once allowed.");
                return;
            }

            // Auto-open the session on first use, then validate the mic device.
            if (!IsSessionOpen && !OpenSession()) return;

            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                NotifyFailed("No microphone device found.");
                return;
            }
            if (!StartMicrophone())
            {
                NotifyFailed("Failed to start microphone capture.");
                return;
            }

            _speechStarted = false;
            _lastVoiceTime = Time.unscaledTime;
            _awaitingFinal = false;
            _streamedSampleCount = 0;
            _utterancePcm.Clear();
            _captureSampleRate = _micClip.frequency;
            // Re-calibrate the adaptive gate from scratch for this session (new mic / room can differ).
            _smoothedLevel = 0f;
            _noiseFloor = 0f;
            _openGate = 0f;
            _closeGate = 0f;
            _noiseFloorInitialized = false;
            MicrophoneEnabled = true;

            Log($"Microphone ENABLED — {(AutoStopOnSilence ? "armed (voice-activity)" : "capturing")} " +
                $"on '{_micDevice}' (session {(_socketOpen ? "open" : "connecting")}, silence: {_silenceThresholdSeconds}s, " +
                $"gate: {(_autoAdjustSensitivity ? $"auto x{_autoSensitivityMargin:0.0} over noise floor" : $"manual amp<{_silenceAmplitudeThreshold}")}).");
        }

        // Explicit mic-off (push-to-talk release, or the master toggle turning off): commit any
        // in-progress utterance and STOP the microphone. The session (socket) stays open.
        private void DisableMicrophone()
        {
            if (!MicrophoneEnabled) return;

            StopMicrophone();
            MicrophoneEnabled = false;
            CurrentInputLevel = 0f;

            FinalizeUtterance(keepMicOpen: false);
        }

        // Commits the current utterance (if any audio was streamed). In voice-activity mode this fires
        // on silence and leaves the mic OPEN (armed) for the next utterance; on explicit mic-off the
        // mic is already stopped.
        private void FinalizeUtterance(bool keepMicOpen)
        {
            // Streamed audio is always resampled to 16 kHz before sending (see CaptureAndStreamAudio),
            // so _streamedSampleCount / _utterancePcm are in the 16 kHz domain — NOT _captureSampleRate.
            int rate = RequiredSampleRate;
            float streamedSeconds = _streamedSampleCount / (float)rate;

            if (_streamedSampleCount > 0 && streamedSeconds < MinCommitSeconds)
            {
                // Below the server's minimum — committing would just be rejected. Discard locally.
                Log($"Utterance too short ({streamedSeconds:F2}s < {MinCommitSeconds}s) — discarded, not committed.");
            }
            else if (_streamedSampleCount > 0 && _socketOpen && !_awaitingFinal)
            {
                // Commit=true finalizes THIS utterance; the socket stays open for the next one.
                EnqueueAudioChunk(Array.Empty<byte>(), commit: true);
                ClearSettling();   // fresh utterance — no transcript held yet
                _awaitingFinal = true;
                _commitSentTime = Time.unscaledTime;
                Log($"Utterance committed ({streamedSeconds:F2}s streamed), awaiting transcript. " +
                    (keepMicOpen ? "Mic stays OPEN (armed for next utterance)." : "Mic stopped. Session OPEN."));
                // Persist exactly what we sent to STT, so a wrong transcript can be listened back.
                DumpUtteranceAudio(rate);
            }
            else if (!keepMicOpen)
            {
                Log("Mic disabled — no captured voice to commit.");
            }

            _speechStarted = false;
            _streamedSampleCount = 0;
            _utterancePcm.Clear();
        }

        // Writes the current utterance's streamed PCM to a 16-bit mono WAV under
        // Application.persistentDataPath/<_sttAudioFolderName> (on Windows that resolves to
        // ...\AppData\LocalLow\<company>\<product>\STT_Audio). Best-effort: never throws into the flow.
        private void DumpUtteranceAudio(int sampleRate)
        {
            if (!_dumpSttAudio || _utterancePcm.Count == 0) return;

            try
            {
                string folder = Path.Combine(Application.persistentDataPath,
                    string.IsNullOrWhiteSpace(_sttAudioFolderName) ? "STT_Audio" : _sttAudioFolderName);
                Directory.CreateDirectory(folder);

                string fileName = $"stt_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.wav";
                string path = Path.Combine(folder, fileName);
                File.WriteAllBytes(path, BuildWav(_utterancePcm.ToArray(), sampleRate));
                Log($"STT audio dumped → {path}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} Could not write STT audio dump: {ex.Message}");
            }
        }

        // Wraps raw 16-bit mono little-endian PCM in a canonical 44-byte WAV/PCM header.
        private static byte[] BuildWav(byte[] pcm, int sampleRate)
        {
            const short channels = 1;
            const short bitsPerSample = 16;
            int byteRate = sampleRate * channels * bitsPerSample / 8;
            short blockAlign = (short)(channels * bitsPerSample / 8);

            using (var ms = new MemoryStream(44 + pcm.Length))
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + pcm.Length);                    // ChunkSize
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);                                 // Subchunk1Size (PCM)
                w.Write((short)1);                           // AudioFormat = PCM
                w.Write(channels);
                w.Write(sampleRate);
                w.Write(byteRate);
                w.Write(blockAlign);
                w.Write(bitsPerSample);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(pcm.Length);                         // Subchunk2Size
                w.Write(pcm);
                w.Flush();
                return ms.ToArray();
            }
        }

        // --- ISTTModel compatibility (delegate to the mic toggle) ------------------

        /// <inheritdoc/>
        public void StartListening() => SetMicrophoneEnabled(true);

        /// <inheritdoc/>
        public void StopListening() => SetMicrophoneEnabled(false);

        private void Log(string message)
        {
            if (_verboseLogging) Debug.Log($"{LogPrefix} {message}");
        }

        // --- Per-frame: drain cross-thread results, capture audio, check timeouts ---

        private void Update()
        {
            while (_mainThreadQueue.TryDequeue(out Action action))
            {
                action?.Invoke();
            }

            if (MicrophoneEnabled && _socketOpen)
            {
                CaptureAndStreamAudio();
            }

            // A transcript is in hand and the server has stopped improving it (or hit the hard cap) —
            // emit the refined result. Checked before the timeout below, which only guards the case
            // where NOTHING came back at all.
            if (_settling && Time.unscaledTime >= _settleDeadline)
            {
                EmitSettledTranscript();
            }
            // Utterance-level timeout: the socket is alive but no transcript came back. Surface it
            // but KEEP THE SESSION OPEN so the next utterance can proceed.
            else if (_awaitingFinal && !_settling && Time.unscaledTime - _commitSentTime > _responseTimeoutSeconds)
            {
                _awaitingFinal = false;
                Debug.LogWarning("ElevenLabsScribeClient: Timed out waiting for transcription response.");
                OnTranscriptionFailed?.Invoke("Timed out waiting for transcription response.");
            }
        }

        // --- Microphone capture (main thread) --------------------------------------

        private bool StartMicrophone()
        {
            _micDevice = ResolveMicrophoneDevice();
            if (string.IsNullOrEmpty(_micDevice)) return false;

            // 1-second ring buffer, looping, at the rate Scribe expects.
            _micClip = Microphone.Start(_micDevice, true, 1, RequiredSampleRate);
            if (_micClip == null) return false;

            _clipSamples = _micClip.samples;
            _readBuffer = new float[_clipSamples];
            _lastReadPos = 0;

            if (_micClip.frequency != RequiredSampleRate)
            {
                // The device forced a different rate. We tag chunks with the real rate, but
                // audio_format=pcm_16000 expects 16 kHz — quality/recognition may suffer.
                Debug.LogWarning($"ElevenLabsScribeClient: mic opened at {_micClip.frequency} Hz, " +
                                 $"expected {RequiredSampleRate} Hz.");
            }
            return true;
        }

        // Picks the configured device if it's currently connected, otherwise the first available one.
        private string ResolveMicrophoneDevice()
        {
            string[] devices = Microphone.devices;
            if (devices == null || devices.Length == 0) return null;

            if (!string.IsNullOrEmpty(_microphoneDevice) && Array.IndexOf(devices, _microphoneDevice) >= 0)
                return _microphoneDevice;

            if (!string.IsNullOrEmpty(_microphoneDevice))
                Debug.LogWarning($"ElevenLabsScribeClient: microphone '{_microphoneDevice}' not connected; " +
                                 $"using '{devices[0]}'.");
            return devices[0];
        }

        private void StopMicrophone()
        {
            if (!string.IsNullOrEmpty(_micDevice) && Microphone.IsRecording(_micDevice))
                Microphone.End(_micDevice);
            _micClip = null;
        }

        private void CaptureAndStreamAudio()
        {
            if (_micClip == null) return;

            int currentPos = Microphone.GetPosition(_micDevice);
            if (currentPos < 0 || currentPos == _lastReadPos) return;

            _micClip.GetData(_readBuffer, 0);

            // Collect samples written since the last read, handling ring-buffer wrap.
            int newCount = currentPos >= _lastReadPos
                ? currentPos - _lastReadPos
                : (_clipSamples - _lastReadPos) + currentPos;

            float[] chunk = new float[newCount];
            float peak = 0f;
            for (int i = 0; i < newCount; i++)
            {
                float sample = _readBuffer[(_lastReadPos + i) % _clipSamples];
                chunk[i] = sample;
                float abs = sample < 0f ? -sample : sample;
                if (abs > peak) peak = abs;
            }
            _lastReadPos = currentPos;

            // Envelope-follow the peak: snap up instantly, decay slowly — a steady level (and meter) instead
            // of frame-to-frame jitter. Still peak domain, so the manual gate stays comparable.
            _smoothedLevel = peak >= _smoothedLevel ? peak : Mathf.Lerp(_smoothedLevel, peak, 0.15f);
            CurrentInputLevel = _smoothedLevel;

            // Effective voice gate. AUTO: derive it from a tracked ambient noise floor so it self-calibrates
            // per mic/room (Discord-style automatic sensitivity). MANUAL: the fixed serialized threshold.
            // Hysteresis (open > close) stops the gate chattering on the quiet dips between words.
            float openGate, closeGate;
            if (_autoAdjustSensitivity)
            {
                if (!_noiseFloorInitialized) { _noiseFloor = _smoothedLevel; _noiseFloorInitialized = true; }
                _openGate = Mathf.Clamp(_noiseFloor * _autoSensitivityMargin + 0.006f, 0.008f, 0.2f);
                _closeGate = _openGate * 0.6f;
                openGate = _openGate;
                closeGate = _closeGate;
            }
            else
            {
                openGate = closeGate = _silenceAmplitudeThreshold;
            }

            // Once speaking, hold the gate open down to the lower close-gate; before speech, require the open-gate.
            bool voiced = _speechStarted ? _smoothedLevel >= closeGate : _smoothedLevel >= openGate;

            // Track the noise floor ONLY while the gate is closed (silence), so speech bursts don't inflate it:
            // rise slowly toward a creeping ambient level, fall quickly when the room gets quieter.
            if (_autoAdjustSensitivity && !voiced)
            {
                float rate = _smoothedLevel > _noiseFloor ? 0.02f : 0.2f;
                _noiseFloor = Mathf.Lerp(_noiseFloor, _smoothedLevel, rate);
            }

            // In voice-activity mode (AutoStopOnSilence) capture begins at the FIRST voiced chunk and
            // then streams the utterance CONTIGUOUSLY until the silence-commit — so the natural sub-
            // threshold dips between syllables/words are kept and the audio isn't fragmented. Only the
            // idle audio BEFORE speech starts (leading dead air / background silence) is dropped. In
            // push-to-talk mode the full held audio is streamed.
            bool sendChunk = !AutoStopOnSilence || _speechStarted || voiced;

            // Stream only what passes the gate — no leading/idle silence, just the utterance.
            // (Skip while awaiting the final result after a commit.)
            if (!_awaitingFinal && sendChunk)
            {
                // We declared audio_format=pcm_16000, but Unity often opens the mic at the device's
                // native rate (e.g. 48 kHz on a Quest), so resample to 16 kHz before sending — otherwise
                // Scribe decodes the PCM at the wrong speed and returns a completely wrong transcript.
                float[] sendSamples = ResampleTo16k(chunk, _captureSampleRate > 0 ? _captureSampleRate : RequiredSampleRate);
                byte[] pcm = FloatsToPcm16(sendSamples);
                EnqueueAudioChunk(pcm, commit: false);
                _streamedSampleCount += sendSamples.Length;
                // Keep a copy of EXACTLY what we send, for the optional .wav debug dump.
                if (_dumpSttAudio) _utterancePcm.AddRange(pcm);
            }

            // Voice-activity segmentation: capture "starts" on the first voiced chunk and "stops"
            // after _silenceThresholdSeconds of quiet, which commits the utterance WITHOUT closing
            // the mic — it stays open (armed) for the next utterance.
            if (voiced)
            {
                if (!_speechStarted)
                    Log($"Speech DETECTED (level {_smoothedLevel:F3} >= gate {openGate:F3}, floor {_noiseFloor:F3}, " +
                        $"{(_autoAdjustSensitivity ? "auto" : "manual")}) — capturing.");
                _speechStarted = true;
                _lastVoiceTime = Time.unscaledTime;
            }
            else if (AutoStopOnSilence && _speechStarted && !_awaitingFinal
                     && Time.unscaledTime - _lastVoiceTime >= _silenceThresholdSeconds)
            {
                Log($"Silence for {_silenceThresholdSeconds}s after speech — committing (mic stays open).");
                FinalizeUtterance(keepMicOpen: true);
            }
        }

        // Linear-resamples a mono float chunk to 16 kHz. ElevenLabs Scribe is told audio_format=pcm_16000,
        // so whatever rate Unity actually opened the mic at, the PCM we send must be 16 kHz or Scribe
        // decodes it at the wrong speed (garbage transcript on otherwise-clear audio). Per-chunk linear
        // interpolation: the tiny boundary artifact every frame is inaudible to the STT model, unlike a
        // 3x speed error. src[0]→dst[0] and src[last]→dst[last] so overall duration is preserved.
        private static float[] ResampleTo16k(float[] src, int srcRate)
        {
            if (srcRate == RequiredSampleRate || srcRate <= 0 || src.Length < 2) return src;

            int outLen = Mathf.Max(1, Mathf.RoundToInt(src.Length * (float)RequiredSampleRate / srcRate));
            var dst = new float[outLen];
            float step = (float)(src.Length - 1) / Mathf.Max(1, outLen - 1);
            for (int i = 0; i < outLen; i++)
            {
                float pos = i * step;
                int i0 = (int)pos;
                int i1 = i0 + 1 < src.Length ? i0 + 1 : src.Length - 1;
                float frac = pos - i0;
                dst[i] = src[i0] * (1f - frac) + src[i1] * frac;
            }
            return dst;
        }

        private static byte[] FloatsToPcm16(float[] samples)
        {
            var bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                short s = (short)Mathf.RoundToInt(clamped * short.MaxValue);
                bytes[i * 2] = (byte)(s & 0xFF);
                bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            return bytes;
        }

        private void EnqueueAudioChunk(byte[] pcm, bool commit)
        {
            var chunk = new ScribeAudioChunk
            {
                message_type = "input_audio_chunk",
                audio_base_64 = pcm.Length > 0 ? Convert.ToBase64String(pcm) : string.Empty,
                commit = commit,
                sample_rate = RequiredSampleRate   // audio is resampled to 16 kHz before enqueue
            };
            _outgoing.Enqueue(JsonUtility.ToJson(chunk));
        }

        // --- WebSocket lifecycle (background) --------------------------------------

        // The socket is a LOCAL owned entirely by this task — never a shared field — so overlapping
        // connections (during a reconnect) can't stomp on each other. Teardown cancels the token,
        // which unwinds the loops and disposes the socket in the finally.
        private async Task RunSocketAsync(int epoch, CancellationToken token)
        {
            ClientWebSocket socket = null;
            try
            {
                socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("xi-api-key", _config.ApiKey);
                // Protocol-level pings keep the connection alive through proxies / idle periods.
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

                var uri = new Uri(BuildConnectUrl());

                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    connectCts.CancelAfter(TimeSpan.FromSeconds(_connectionTimeoutSeconds));
                    await socket.ConnectAsync(uri, connectCts.Token);
                }

                // Publish "connected" on the main thread, but only if this attempt is still current.
                PostToMainThread(() =>
                {
                    if (epoch != _sessionEpoch) return;   // superseded/torn down while connecting
                    _socketOpen = true;
                    _connecting = false;
                    if (_micClip != null) _lastReadPos = Microphone.GetPosition(_micDevice);
                    Log("WebSocket CONNECTED — session open, streaming when mic enabled.");
                });

                await Task.WhenAll(SendLoopAsync(socket, token), ReceiveLoopAsync(socket, epoch, token));

                // Both loops ended without us cancelling on purpose → the connection dropped.
                if (!token.IsCancellationRequested)
                    PostSessionFail(epoch, "Connection ended unexpectedly.");
            }
            catch (OperationCanceledException)
            {
                // token cancelled = intentional teardown (no failure). Otherwise the linked
                // connect-timeout fired.
                if (!token.IsCancellationRequested)
                    PostSessionFail(epoch, "WebSocket connection timed out.");
            }
            catch (Exception ex)
            {
                PostSessionFail(epoch, $"WebSocket error: {ex.Message}");
            }
            finally
            {
                if (socket != null)
                {
                    try { socket.Abort(); } catch { }
                    socket.Dispose();
                }
            }
        }

        private string BuildConnectUrl()
        {
            // Model ID comes from the ElevenLabsConfig SO (falls back to the default if unconfigured).
            string modelId = _config != null ? _config.SttModelId : ElevenLabsConfig.DefaultSttModelId;

            // Language code is a manager-level setting now (STTManager), not per-client.
            string languageCode = _stt != null ? _stt.LanguageCode : null;

            var sb = new StringBuilder(Endpoint);
            sb.Append("?model_id=").Append(modelId);
            sb.Append("&audio_format=pcm_16000");
            sb.Append("&commit_strategy=manual");
            if (!string.IsNullOrWhiteSpace(languageCode))
                sb.Append("&language_code=").Append(Uri.EscapeDataString(languageCode));
            return sb.ToString();
        }

        private async Task SendLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                if (_outgoing.TryDequeue(out string json))
                {
                    byte[] payload = Encoding.UTF8.GetBytes(json);
                    await socket.SendAsync(new ArraySegment<byte>(payload),
                        WebSocketMessageType.Text, true, token);
                }
                else
                {
                    await Task.Delay(10, token);
                }
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, int epoch, CancellationToken token)
        {
            var buffer = new byte[8192];
            var message = new StringBuilder();

            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                }
                catch (OperationCanceledException) { break; }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // The server closed the socket — surface the reason instead of hanging silently.
                    string status = result.CloseStatus.HasValue
                        ? $"{(int)result.CloseStatus.Value} {result.CloseStatus.Value}"
                        : "no status";
                    string desc = string.IsNullOrEmpty(result.CloseStatusDescription)
                        ? "" : $" — {result.CloseStatusDescription}";
                    PostSessionFail(epoch, $"Server closed the WebSocket ({status}){desc}.");
                    break;
                }

                message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                string json = message.ToString();
                message.Clear();
                HandleServerMessage(json, epoch);
            }
        }

        private void HandleServerMessage(string json, int epoch)
        {
            ScribeServerMessage msg;
            try { msg = JsonUtility.FromJson<ScribeServerMessage>(json); }
            catch { return; }
            if (msg == null || string.IsNullOrEmpty(msg.message_type)) return;

            // Diagnostic: surface every inbound message (type + any text/error) when verbose logging is
            // on, so a stuck utterance (e.g. only partials, no committed_transcript) is visible.
            Log($"◄ server '{msg.message_type}'" +
                (string.IsNullOrEmpty(msg.text) ? "" : $" text=\"{msg.text}\"") +
                (string.IsNullOrEmpty(msg.error) ? "" : $" error=\"{msg.error}\""));

            switch (msg.message_type)
            {
                case MsgPartialTranscript:
                    // Live partial (not committed) — surface for a caption only. Epoch-guarded + on the
                    // main thread, and entirely isolated from the committed/settle path.
                    string partial = msg.text;
                    PostToMainThread(() => { if (epoch == _sessionEpoch) OnPartialTranscript?.Invoke(partial); });
                    break;

                case MsgSessionStarted:
                    // Handshake: nothing to surface.
                    break;

                case MsgCommittedTranscript:
                case MsgCommittedTranscriptTimestamps:
                    // Do NOT complete here: later messages for the same utterance carry the server's
                    // refined transcript. OfferTranscript keeps the newest and completes once it settles.
                    string text = msg.text;
                    PostToMainThread(() => { if (epoch == _sessionEpoch) OfferTranscript(text); });
                    break;

                default:
                    // A server error MESSAGE (e.g. "commit request ignored") is per-utterance, not a
                    // dead connection — the socket is still open, so keep the SESSION alive. Only an
                    // actual socket close (handled in ReceiveLoop) tears the session down.
                    string reason = string.IsNullOrEmpty(msg.error) ? msg.message_type : msg.error;
                    PostUtteranceFail(epoch, $"Scribe error: {reason}");
                    break;
            }
        }

        // --- Completion / failure (main thread) ------------------------------------

        // A committed transcript for the current utterance arrived. Rather than firing it straight away,
        // hold it as the best-so-far and wait out a short settle window: Scribe keeps refining the same
        // committed audio and re-emits it, so the LAST transcript we receive is the most accurate one.
        // Each refinement restarts the window, bounded by _transcriptSettleMaxSeconds from the first.
        private void OfferTranscript(string text)
        {
            if (!_awaitingFinal) return;   // stray/duplicate outside an utterance — ignore

            string incoming = text ?? string.Empty;

            // Never let a blank refinement erase a real transcript we already hold.
            if (_settling && string.IsNullOrWhiteSpace(incoming) && !string.IsNullOrWhiteSpace(_settledTranscript))
            {
                Log("Ignoring an empty refinement — keeping the transcript already received.");
                return;
            }

            float now = Time.unscaledTime;

            if (!_settling)
            {
                _settling = true;
                _settleRevisions = 0;
                _settleStartTime = now;
                _settleHardStop = now + _transcriptSettleMaxSeconds;
                Log($"Transcript RECEIVED ({incoming.Length} chars): \"{incoming}\" — " +
                    $"waiting up to {_transcriptSettleSeconds:F2}s for a refined version.");
            }
            else if (incoming == _settledTranscript)
            {
                // Identical text (Scribe re-sends the same string as the _with_timestamps variant).
                // Nothing improved, so it must not buy the server another settle window.
                Log("Duplicate transcript — no change, still settling.");
                return;
            }
            else
            {
                _settleRevisions++;
                Log($"Transcript REFINED (#{_settleRevisions} at +{now - _settleStartTime:F2}s, " +
                    $"{incoming.Length} chars): \"{incoming}\".");
            }

            _settledTranscript = incoming;
            // Restart the window so a stream of refinements is absorbed, but never past the hard stop.
            _settleDeadline = Mathf.Min(now + _transcriptSettleSeconds, _settleHardStop);
        }

        // The settle window expired (or the session is ending): fire the best transcript we hold and KEEP
        // THE SESSION OPEN for the next utterance. Returns true when a transcript was emitted.
        private bool EmitSettledTranscript()
        {
            if (!_settling) return false;

            string result = _settledTranscript ?? string.Empty;
            float waited = Time.unscaledTime - _settleStartTime;
            // We stopped because the CAP ran out, not because the server went quiet — so a better
            // transcript may well have been on its way. Say so loudly; the fix is a bigger cap.
            bool truncated = _settleRevisions > 0 && Time.unscaledTime >= _settleHardStop;

            _settling = false;
            _settledTranscript = null;
            _awaitingFinal = false;

            if (truncated)
                Debug.LogWarning($"{LogPrefix} Transcript settling hit the {_transcriptSettleMaxSeconds:F1}s cap " +
                                 $"with {_settleRevisions} refinement(s) still arriving — the server had not " +
                                 "finished improving it. Raise Transcript Settle Max Seconds.");

            Log($"Transcript SETTLED after {_settleRevisions} refinement(s) in {waited:F2}s " +
                $"({result.Length} chars): \"{result}\". Session stays OPEN.");
            OnTranscriptionComplete?.Invoke(result);
            return true;
        }

        // Drops any in-flight settle without emitting (session teardown).
        private void ClearSettling()
        {
            _settling = false;
            _settledTranscript = null;
            _settleRevisions = 0;
        }

        // Pre-session validation failure (consent/config/mic). Nothing to tear down.
        private void NotifyFailed(string reason)
        {
            Debug.LogWarning($"ElevenLabsScribeClient: {reason}");
            OnTranscriptionFailed?.Invoke(reason);
        }

        // Per-utterance failure (e.g. server rejected a commit). The SESSION stays open — just clear
        // the pending state so the next utterance can proceed on the same connection.
        private void FailUtterance(string reason)
        {
            // A late per-utterance error must not throw away a transcript the server already gave us.
            if (EmitSettledTranscript())
            {
                Debug.LogWarning($"ElevenLabsScribeClient: {reason} (a transcript had already arrived — using it).");
                return;
            }
            _awaitingFinal = false;
            Debug.LogWarning($"ElevenLabsScribeClient: {reason}");
            OnTranscriptionFailed?.Invoke(reason);
        }

        // Fatal SESSION failure (socket died / server closed). Tears the whole session down.
        private void FailSession(string reason)
        {
            bool notify = _awaitingFinal || MicrophoneEnabled || _connecting || _socketOpen;
            Debug.LogWarning($"ElevenLabsScribeClient: {reason}");

            // The socket dying mid-settle still leaves us holding a valid transcript — deliver it
            // instead of failing the turn, then tear the session down quietly.
            bool delivered = EmitSettledTranscript();

            TeardownSocket();
            if (notify && !delivered) OnTranscriptionFailed?.Invoke(reason);
        }

        // Tears down mic + socket without firing events. Safe to call repeatedly. Bumping the epoch
        // invalidates any in-flight callbacks from the connection being torn down. Cancelling the
        // token unwinds RunSocketAsync, which disposes its own socket in its finally.
        private void TeardownSocket()
        {
            _sessionEpoch++;
            MicrophoneEnabled = false;
            _socketOpen = false;
            _connecting = false;
            _awaitingFinal = false;
            ClearSettling();
            CurrentInputLevel = 0f;

            StopMicrophone();

            CancellationTokenSource cts = _cts;
            _cts = null;
            try { cts?.Cancel(); } catch { /* already disposed */ }
            cts?.Dispose();
        }

        // --- Consent reaction ------------------------------------------------------

        private void HandleConsentChanged(ConsentChangedEvent evt)
        {
            // Stop the whole session immediately if consent/voice is no longer available.
            if (IsSessionOpen && (_consent == null || !_consent.IsVoiceModeAvailable()))
            {
                FailSession("Voice mode revoked mid-session.");
            }
            else if (_autoStart && !MicrophoneEnabled
                     && _consent != null
                     && _consent.IsVoiceModeAvailable())
            {
                SetMicrophoneEnabled(true);
            }
        }

        // --- Cross-thread helpers --------------------------------------------------

        private void PostToMainThread(Action action) => _mainThreadQueue.Enqueue(action);

        // Only fails the session if the reporting connection is still the current one — a stale
        // failure from a dropped connection must not tear down a freshly reconnected session.
        private void PostSessionFail(int epoch, string reason) =>
            _mainThreadQueue.Enqueue(() => { if (epoch == _sessionEpoch) FailSession(reason); });

        // Per-utterance failure (session-preserving), epoch-guarded like PostSessionFail.
        private void PostUtteranceFail(int epoch, string reason) =>
            _mainThreadQueue.Enqueue(() => { if (epoch == _sessionEpoch) FailUtterance(reason); });

        // --- Serializable DTOs (JsonUtility) ---------------------------------------

        [Serializable]
        private class ScribeAudioChunk
        {
            public string message_type;
            public string audio_base_64;
            public bool commit;
            public int sample_rate;
        }

        [Serializable]
        private class ScribeServerMessage
        {
            public string message_type;
            public string text;
            public string error;
        }
    }
}
