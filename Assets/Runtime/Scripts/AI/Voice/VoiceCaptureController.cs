using System;
using System.IO;
using System.Text;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DumyatRuntime
{
    /// <summary>
    /// Reusable voice-capture controller: the single home for the capture-control features —
    /// capture mode (push-to-talk / voice-activity),
    /// push-to-talk release tail, voice-activity amplitude + silence tuning, reconnect cooldown,
    /// mic/session status, and an Arabic-safe UTF-8 debug log. It drives an
    /// <see cref="IVoiceCaptureSource"/> (e.g. <see cref="ElevenLabsScribeClient"/>) directly so both
    /// the test harness AND the real merchant negotiation share one implementation.
    ///
    /// TWO USAGE MODES (see <see cref="_autoReArm"/>):
    ///   • CONTINUOUS (harness): arm once, keep listening — voice-activity re-arms automatically after
    ///     each utterance; push-to-talk stays armed for the next press.
    ///   • TURN-BASED (negotiation): each <see cref="BeginCapture"/> captures ONE utterance, then the
    ///     mic is disabled (so it is not listening while the merchant thinks + speaks); the caller
    ///     re-arms with the next <see cref="BeginCapture"/>.
    ///
    /// SESSION vs CAPTURE: <see cref="OpenSession"/>/<see cref="CloseSession"/> own the persistent
    /// transport; <see cref="BeginCapture"/>/<see cref="EndCapture"/> arm/disarm capturing an utterance
    /// within the open session. Results arrive via <see cref="OnTranscription"/> /
    /// <see cref="OnCaptureFailed"/>.
    /// </summary>
    public class VoiceCaptureController : MonoBehaviour
    {
        public enum CaptureMode { PushToTalk, VoiceActivity }

        // Injected STT service, used to resolve a capture source from its model pool. Optional — a
        // runtime-added controller isn't auto-injected, in which case EnsureSource falls back to a
        // scene lookup (see EnsureSource).
        private ISTTService _stt;

        [Inject]
        private void Inject(ISTTService stt) => _stt = stt;

        [Header("STT Source")]
        [Tooltip("The voice-capture source to drive. Assign the ElevenLabsScribeClient here; if left " +
                 "empty it is resolved from the STTManager pool or the scene on first use.")]
        [SerializeField] private ElevenLabsScribeClient _scribeClient;

        [Header("Microphone Device")]
        [Tooltip("Preferred microphone. Empty = system default (first connected device). The Inspector " +
                 "shows a live list that updates as microphones are connected/disconnected. Pushed to " +
                 "the STT source before each capture.")]
        [SerializeField] private string _microphoneDevice = "";

        [Header("Capture Mode")]
        [Tooltip("Push-To-Talk: hold to capture, release to send. Voice-Activity: auto-captures speech " +
                 "and commits on silence.")]
        [SerializeField] private CaptureMode _captureMode = CaptureMode.VoiceActivity;

        [Tooltip("CONTINUOUS (true): keep listening across utterances (harness). TURN-BASED (false): " +
                 "capture one utterance per BeginCapture, then stop until re-armed (negotiation).")]
        [SerializeField] private bool _autoReArm = true;

        [Header("Controls (new Input System)")]
        [Tooltip("Display label for the push-to-talk control (the real binding lives on the " +
                 "InputSystem_Actions 'PushToTalk' action).")]
        [SerializeField] private string _controlBinding = "PushToTalk";

        [Tooltip("Push-to-talk only: after releasing the button, keep capturing for this many seconds " +
                 "so the trailing audio of the last word is included. 0 = stop immediately on release.")]
        [SerializeField, Min(0f)] private float _pttReleaseTailSeconds = 0.35f;

        [Header("Voice-Activity Tuning")]
        [Tooltip("Discord-style automatic input sensitivity: the source tracks the ambient noise floor and " +
                 "rides the voice gate above it, self-calibrating per microphone/room. When on, the fixed " +
                 "threshold below is ignored (kept as a fallback). ON by default.")]
        [SerializeField] private bool _autoAdjustSensitivity = true;

        [Tooltip("Peak amplitude (0-1) below which audio is treated as silence. Applied to the source " +
                 "on each capture. Only used when Auto Adjust Sensitivity is OFF.")]
        [SerializeField, Range(0f, 0.5f)] private float _vadAmplitudeThreshold = 0.02f;

        [Tooltip("Seconds of continuous silence after speech before the utterance is finalized.")]
        [SerializeField, Min(0.1f)] private float _vadSilenceSeconds = 1.2f;

        [Tooltip("After a session drop, wait this long before auto-reconnecting (voice-activity), so a " +
                 "failing reconnect backs off instead of hammering every frame.")]
        [SerializeField, Min(0f)] private float _reconnectCooldownSeconds = 2f;

        [Header("Speech Limit")]
        [Tooltip("Hard cap on ONE player utterance, in seconds. On reaching it the mic commits early " +
                 "(the audio captured so far is still transcribed) so a long ramble cannot burn " +
                 "unbounded STT usage. Applies to both capture modes. 0 = no cap.")]
        [SerializeField, Min(0f)] private float _maxSpeechSeconds = 20f;

        [Header("Debug Log File")]
        [Tooltip("Write transcripts + status/errors (and, via AppendLog, merchant lines) to a UTF-8 " +
                 "text file so the Arabic/Egyptian output can be reviewed outside the Unity Console. " +
                 "Saved under Application.persistentDataPath and appended across sessions.")]
        [SerializeField] private bool _logToFile = true;

        [Tooltip("Name of the debug log file written (UTF-8, appended) under Application.persistentDataPath.")]
        [SerializeField] private string _logFileName = "stt_debug_log.txt";

        [Header("Debug UI")]
        [Tooltip("Show a temporary on-screen + in-headset overlay to inspect and drive mic capture — " +
                 "device list, OS permission, session state, and a live input-level meter (see " +
                 "VoiceCaptureDebugUI). Can be toggled at runtime. Leave OFF for normal play.")]
        [SerializeField] private bool _showDebugUI;

        private VoiceCaptureDebugUI _debugUI;
        private bool _debugUIApplied;

        /// <summary>Toggle the temporary voice-capture debug overlay (see <see cref="VoiceCaptureDebugUI"/>).</summary>
        public bool ShowDebugUI
        {
            get => _showDebugUI;
            set { _showDebugUI = value; ApplyDebugUI(); }
        }

        /// <summary>Fires with the final transcript of a captured utterance.</summary>
        public event Action<string> OnTranscription;

        /// <summary>
        /// Fires repeatedly mid-utterance with the latest partial (not-yet-final) transcript, for a live
        /// caption. Display only — never route it into the negotiation decision.
        /// </summary>
        public event Action<string> OnPartialTranscription;

        /// <summary>Fires with a human-readable reason when a capture/transcription fails.</summary>
        public event Action<string> OnCaptureFailed;

        // --- Resolved source + input ------------------------------------------------
        private IVoiceCaptureSource _source;
        private bool _sourceSubscribed;
        private InputSystem_Actions _inputSystem;
        private bool _logInitialized;
        private string _logFilePath;

        // --- Capture state (main-thread only) --------------------------------------
        private bool _armed;                 // caller wants to capture an utterance
        private bool _startedCapture;        // WE enabled the mic for the current utterance
        private bool _pttHeld;               // true while the PTT control is held
        private bool _pttStopPending;        // PTT released; committing after the tail delay
        private float _pttStopTime;          // unscaled time at which the delayed PTT stop fires
        private float _reconnectAllowedTime; // earliest time we may auto-reconnect after a drop
        private float _utteranceStartTime;   // unscaled time the current utterance started (speech cap)
        private CaptureMode _lastCaptureMode;
        private string _status = "Idle.";
        private string _lastTranscript = "";
        private string _lastError = "";

        // --- Public config (so a driver like the harness can push its Inspector values) ---
        public CaptureMode Mode { get => _captureMode; set => _captureMode = value; }
        public bool AutoReArm { get => _autoReArm; set => _autoReArm = value; }
        public string ControlBinding { get => _controlBinding; set => _controlBinding = value; }
        public float ReleaseTailSeconds { get => _pttReleaseTailSeconds; set => _pttReleaseTailSeconds = Mathf.Max(0f, value); }
        public float VadAmplitudeThreshold { get => _vadAmplitudeThreshold; set => _vadAmplitudeThreshold = Mathf.Clamp(value, 0f, 0.5f); }
        /// <summary>Discord-style auto input sensitivity (adaptive noise-floor gate). Pushed to the source live
        /// and re-applied each utterance. Bind this from a settings menu or the debug panel.</summary>
        public bool AutoAdjustSensitivity
        {
            get => _autoAdjustSensitivity;
            set { _autoAdjustSensitivity = value; if (_source != null) _source.AutoAdjustSensitivity = value; }
        }
        public float VadSilenceSeconds { get => _vadSilenceSeconds; set => _vadSilenceSeconds = Mathf.Max(0.1f, value); }
        public float ReconnectCooldownSeconds { get => _reconnectCooldownSeconds; set => _reconnectCooldownSeconds = Mathf.Max(0f, value); }
        /// <summary>Hard cap on one utterance, in seconds; 0 = no cap. See <see cref="_maxSpeechSeconds"/>.</summary>
        public float MaxSpeechSeconds { get => _maxSpeechSeconds; set => _maxSpeechSeconds = Mathf.Max(0f, value); }
        public bool LogToFile { get => _logToFile; set => _logToFile = value; }
        /// <summary>Log file name; only takes effect before the first log line is written.</summary>
        public string LogFileName { get => _logFileName; set => _logFileName = value; }
        /// <summary>Preferred microphone device; empty = system default. Applied to the source per capture.</summary>
        public string MicrophoneDevice { get => _microphoneDevice; set => _microphoneDevice = value ?? string.Empty; }

        /// <summary>Names of the microphone devices currently connected to the system (live).</summary>
        public static string[] AvailableMicrophoneDevices => Microphone.devices;

        /// <summary>Explicitly assign the capture source, overriding pool/scene resolution.</summary>
        public void AssignSource(ElevenLabsScribeClient client)
        {
            _scribeClient = client;
            if (client != null) SetSource(client);
        }

        // --- Public status (for meters / UI) ---------------------------------------
        public bool IsSessionOpen => _source != null && _source.IsSessionOpen;
        public bool IsCapturing => _source != null && _source.MicrophoneEnabled;
        public float CurrentInputLevel => _source != null ? _source.CurrentInputLevel : 0f;
        /// <summary>Tracked ambient noise floor from the source (0 when idle). For the debug readout.</summary>
        public float CurrentNoiseFloor => _source != null ? _source.CurrentNoiseFloor : 0f;
        /// <summary>The effective voice gate the source is comparing the level against (adaptive or manual).</summary>
        public float CurrentGateThreshold => _source != null ? _source.CurrentGateThreshold : _vadAmplitudeThreshold;
        public string LastTranscript => _lastTranscript;
        public string LastError => _lastError;
        public string StatusText => _status;
        public string LogFilePath => _logFilePath;

        private void Awake()
        {
            _inputSystem = new InputSystem_Actions();
            _inputSystem.Player.PushToTalk.started += OnTalkPressed;
            _inputSystem.Player.PushToTalk.canceled += OnTalkReleased;
            _lastCaptureMode = _captureMode;
            ApplyDebugUI();   // spawn/enable the debug overlay if the checkbox is on
        }

        // Spawns (or enables/disables) the temporary debug overlay to match the Show Debug UI checkbox.
        // The overlay lives in its own component (VoiceCaptureDebugUI) so this controller stays focused;
        // the checkbox just drives it. Safe to call repeatedly (it syncs the component's enabled state).
        private void ApplyDebugUI()
        {
            _debugUIApplied = _showDebugUI;
            if (_showDebugUI)
            {
                if (_debugUI == null) _debugUI = GetComponent<VoiceCaptureDebugUI>();
                if (_debugUI == null) _debugUI = gameObject.AddComponent<VoiceCaptureDebugUI>();
                _debugUI.enabled = true;
            }
            else if (_debugUI != null)
            {
                _debugUI.enabled = false;
            }
        }

        private void OnEnable() => _inputSystem?.Enable();
        private void OnDisable() => _inputSystem?.Disable();

        private void OnDestroy()
        {
            EndCapture();
            UnsubscribeSource();
            _source?.CloseSession();
            if (_inputSystem != null)
            {
                _inputSystem.Player.PushToTalk.started -= OnTalkPressed;
                _inputSystem.Player.PushToTalk.canceled -= OnTalkReleased;
                _inputSystem.Dispose();
            }
        }

        // --- Public session/capture API --------------------------------------------

        /// <summary>Opens the persistent session (idempotent). In continuous mode also arms capture.</summary>
        public bool OpenSession()
        {
            if (!EnsureSource()) return false;
            bool ok = _source.OpenSession();
            _status = ok ? "Session open." : "Session failed to open.";
            if (ok && _autoReArm) BeginCapture();
            return ok;
        }

        /// <summary>Closes the persistent session and disarms capture.</summary>
        public void CloseSession()
        {
            EndCapture();
            _source?.CloseSession();
            _status = "Session closed.";
        }

        /// <summary>
        /// Arms capture of an utterance. Voice-activity starts capturing immediately (auto-commits on
        /// silence); push-to-talk waits for the button. In turn-based mode this captures exactly one
        /// utterance; in continuous mode it stays armed.
        /// </summary>
        public void BeginCapture()
        {
            if (!EnsureSource()) { NotifyFailed("No STT source available."); return; }

            _armed = true;
            if (!_source.IsSessionOpen) _source.OpenSession();

            if (_captureMode == CaptureMode.VoiceActivity)
                StartUtterance();
            else
                _status = $"Armed — hold '{_controlBinding}' to talk.";
        }

        /// <summary>Disarms and stops any in-progress capture. The session stays open.</summary>
        public void EndCapture()
        {
            _armed = false;
            _startedCapture = false;
            _pttStopPending = false;
            if (_source != null && _source.MicrophoneEnabled) _source.SetMicrophoneEnabled(false);
        }

        /// <summary>
        /// Appends a labelled, timestamped line to the same UTF-8 debug log the transcripts use — so a
        /// caller (e.g. the negotiation flow) can record merchant lines and the whole Arabic
        /// conversation ends up in one reviewable file. No-op when file logging is off.
        /// </summary>
        public void AppendLog(string label, string message) => WriteLog(label, message);

        // --- Per-frame capture driving ---------------------------------------------

        private void Update()
        {
            // React to the Show Debug UI checkbox being toggled at runtime (before the source guard, so
            // the overlay works even when no capture source has been resolved yet).
            if (_showDebugUI != _debugUIApplied) ApplyDebugUI();

            if (_source == null) return;

            // Handle a runtime capture-mode switch: stop any capture left running by the previous mode
            // (voice-activity leaves the mic armed) so the new mode starts clean.
            if (_captureMode != _lastCaptureMode)
            {
                _lastCaptureMode = _captureMode;
                _pttHeld = false;
                _startedCapture = false;
                _pttStopPending = false;
                if (_source.MicrophoneEnabled) _source.SetMicrophoneEnabled(false);
                _status = $"Switched to {_captureMode}.";
            }

            // Speech cap: commit the utterance early once it runs too long, so one rambling answer
            // cannot stream unbounded audio to STT. Checked BEFORE the mode branches because the
            // voice-activity branch returns below and would never reach a check placed after it.
            //
            // EndCapture() (not StopPushToTalkCapture()) is deliberate: it disarms as well as
            // committing. Merely stopping the mic would leave _armed set, and the voice-activity
            // re-arm below would start a fresh utterance on the very next frame — letting the player
            // talk forever in back-to-back capped chunks, which is exactly what the cap must prevent.
            // The flow re-arms for the next turn via BeginListening -> BeginCapture.
            if (_startedCapture && _maxSpeechSeconds > 0f
                && Time.unscaledTime - _utteranceStartTime >= _maxSpeechSeconds)
            {
                EndCapture();
                _status = $"Speech limit ({_maxSpeechSeconds:0}s) reached — sending.";
                AppendLog("STT", $"Player hit the {_maxSpeechSeconds:0}s speech cap; committing early.");
                return;
            }

            if (_captureMode == CaptureMode.VoiceActivity)
            {
                // Arm/re-arm: after a session drop, reconnect only past the cooldown.
                if (_armed && !_source.MicrophoneEnabled && _source.IsSessionOpen
                    && Time.unscaledTime >= _reconnectAllowedTime)
                    StartUtterance();
                return;
            }

            // Push-to-talk: the idle session can be dropped by the server between utterances
            // (~15s idle close). If the player presses the button during that window, reopen the
            // session so the press recovers instead of being silently ignored.
            if (_pttHeld && _armed && !_source.IsSessionOpen
                && Time.unscaledTime >= _reconnectAllowedTime)
            {
                _reconnectAllowedTime = Time.unscaledTime + _reconnectCooldownSeconds;
                _status = "Session dropped — reconnecting for push-to-talk…";
                _source.OpenSession();
            }

            // Push-to-talk drives capture only while armed and the session is open.
            bool held = _pttHeld && _armed && _source.IsSessionOpen;
            bool capturing = _source.MicrophoneEnabled;

            if (held)
            {
                _pttStopPending = false;   // (re)pressed within the tail window — cancel the pending stop
                if (!capturing && !_startedCapture) StartUtterance();
            }
            else if (_startedCapture)
            {
                // Released: keep capturing for a short tail so the last word finishes, then commit.
                if (!_pttStopPending)
                {
                    if (_pttReleaseTailSeconds > 0f)
                    {
                        _pttStopPending = true;
                        _pttStopTime = Time.unscaledTime + _pttReleaseTailSeconds;
                        _status = $"Released — capturing tail ({_pttReleaseTailSeconds:F2}s)…";
                    }
                    else
                    {
                        StopPushToTalkCapture();
                    }
                }
                else if (Time.unscaledTime >= _pttStopTime)
                {
                    StopPushToTalkCapture();
                }
            }
        }

        // Begins capturing one utterance in the open session (configures the source for the mode first).
        private void StartUtterance()
        {
            ConfigureSourceForMode();
            _lastError = "";
            _startedCapture = true;
            _utteranceStartTime = Time.unscaledTime;   // starts this utterance's speech-cap clock
            _status = _captureMode == CaptureMode.PushToTalk
                ? "Capturing… release to send."
                : "Armed — speak; auto-stops on silence.";
            _source.SetMicrophoneEnabled(true);
        }

        // PTT commit: stop capturing this utterance (commit + playback); the session stays open.
        private void StopPushToTalkCapture()
        {
            _startedCapture = false;
            _pttStopPending = false;
            _status = "Processing… awaiting transcript.";
            _source.SetMicrophoneEnabled(false);
        }

        // Point the source at the right device, stop behaviour + VAD tuning for the selected mode.
        private void ConfigureSourceForMode()
        {
            _source.MicrophoneDevice = _microphoneDevice;   // controller owns the device selection

            if (_captureMode == CaptureMode.PushToTalk)
            {
                _source.AutoStopOnSilence = false;
            }
            else
            {
                _source.AutoStopOnSilence = true;
                _source.AutoAdjustSensitivity = _autoAdjustSensitivity;
                _source.SilenceAmplitudeThreshold = _vadAmplitudeThreshold;   // manual fallback when auto is off
                _source.SilenceThresholdSeconds = _vadSilenceSeconds;
            }
        }

        private void OnTalkPressed(InputAction.CallbackContext _) => _pttHeld = true;
        private void OnTalkReleased(InputAction.CallbackContext _) => _pttHeld = false;

        // --- Source resolution + events --------------------------------------------

        // Resolves the capture source lazily: prefer the Inspector reference, else the top-priority
        // available model from the injected STT service pool, else the first scribe client in the scene.
        private bool EnsureSource()
        {
            if (_source != null) return true;

            if (_scribeClient != null)
            {
                SetSource(_scribeClient);
                return true;
            }

            if (_stt != null)
            {
                foreach (ISTTModel model in _stt.Models)
                {
                    if (model is IVoiceCaptureSource vcs && model.IsAvailable)
                    {
                        SetSource(vcs);
                        return true;
                    }
                }
            }

            var scribe = FindFirstObjectByType<ElevenLabsScribeClient>();
            if (scribe != null)
            {
                SetSource(scribe);
                return true;
            }

            return false;
        }

        private void SetSource(IVoiceCaptureSource source)
        {
            if (_source == source) return;
            UnsubscribeSource();
            _source = source;
            if (_source != null)
            {
                _source.OnTranscriptionComplete += HandleSourceTranscription;
                _source.OnPartialTranscript += HandleSourcePartial;
                _source.OnTranscriptionFailed += HandleSourceFailed;
                _sourceSubscribed = true;
            }
        }

        private void UnsubscribeSource()
        {
            if (!_sourceSubscribed || _source == null) return;
            _source.OnTranscriptionComplete -= HandleSourceTranscription;
            _source.OnPartialTranscript -= HandleSourcePartial;
            _source.OnTranscriptionFailed -= HandleSourceFailed;
            _sourceSubscribed = false;
        }

        // Re-raise a live partial for any UI (debug overlay / player caption). Display only.
        private void HandleSourcePartial(string text) => OnPartialTranscription?.Invoke(text);

        private void HandleSourceTranscription(string text)
        {
            _lastTranscript = text ?? "";
            _startedCapture = false;
            WriteLog($"TRANSCRIPT ({_captureMode}, {_lastTranscript.Length} chars)", _lastTranscript);
            _status = $"Transcript received ({_lastTranscript.Length} chars).";

            // Turn-based: stop listening until the caller re-arms. Continuous: voice-activity keeps the
            // mic armed automatically; push-to-talk stays armed for the next press.
            if (!_autoReArm) EndCapture();

            OnTranscription?.Invoke(_lastTranscript);
        }

        private void HandleSourceFailed(string reason)
        {
            _reconnectAllowedTime = Time.unscaledTime + _reconnectCooldownSeconds;
            _startedCapture = false;
            _pttStopPending = false;
            NotifyFailed(reason);
        }

        private void NotifyFailed(string reason)
        {
            _lastError = reason;
            _status = $"Failed: {reason}";
            WriteLog("ERROR", reason);
            OnCaptureFailed?.Invoke(reason);
        }

        // --- Debug log file (UTF-8 so Arabic renders in a normal text editor) -------

        private void EnsureLogInitialized()
        {
            if (_logInitialized || !_logToFile) return;
            _logInitialized = true;

            if (string.IsNullOrWhiteSpace(_logFileName)) _logFileName = "stt_debug_log.txt";
            try
            {
                _logFilePath = Path.Combine(Application.persistentDataPath, _logFileName);
                File.AppendAllText(_logFilePath,
                    $"{Environment.NewLine}===== Voice session {DateTime.Now:yyyy-MM-dd HH:mm:ss} " +
                    $"(mode: {_captureMode}) ====={Environment.NewLine}",
                    Encoding.UTF8);
                Debug.Log($"[VoiceCapture] Debug log → {_logFilePath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VoiceCapture] Could not open debug log file: {ex.Message}");
                _logFilePath = null;
            }
        }

        private void WriteLog(string label, string message)
        {
            if (!_logToFile) return;
            EnsureLogInitialized();
            if (string.IsNullOrEmpty(_logFilePath)) return;
            try
            {
                File.AppendAllText(_logFilePath,
                    $"[{DateTime.Now:HH:mm:ss}] {label}: {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VoiceCapture] Failed to write debug log: {ex.Message}");
            }
        }
    }
}
