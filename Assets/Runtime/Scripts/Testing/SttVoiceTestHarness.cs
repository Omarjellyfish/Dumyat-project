using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DumyatRuntime
{
    /// <summary>
    /// TEST-ONLY: isolates the voice → Speech-to-Text path from the merchant negotiation
    /// pipeline, so mic recording quality and the returned transcript can be judged on their own.
    ///
    /// TWO LEVELS OF CONTROL:
    ///   • MICROPHONE — a serialized boolean (<see cref="_microphoneEnabled"/>). Toggling it ON opens
    ///     the persistent STT session (WebSocket) and keeps it open; OFF closes it. Safe to flip in
    ///     the Inspector at runtime.
    ///   • CAPTURE — push-to-talk / voice-activity. This only starts/stops capturing an UTTERANCE
    ///     inside the already-open session, and does nothing while the microphone bool is OFF.
    ///
    /// It also grants AI consent and disables any <see cref="MerchantSystemTestDriver"/> so nothing
    /// else contends for the mic. Input uses the new Input System (the generated
    /// <c>InputSystem_Actions.Player.PushToTalk</c> action). Results + a live input-level meter are
    /// drawn via OnGUI. Not part of the shipping game.
    /// </summary>
    public class SttVoiceTestHarness : MonoBehaviour
    {
        private enum CaptureMode { PushToTalk, VoiceActivity }

        [Header("Setup")]
        [Tooltip("Auto-grant mic + internet consent on Play so the STT path is reachable.")]
        [SerializeField] private bool _grantConsentOnPlay = true;

        [Tooltip("Disable any MerchantSystemTestDriver in the scene so the negotiation test does " +
                 "not also start and contend for the microphone / STTManager.")]
        [SerializeField] private bool _disableMerchantTest = true;

        [Tooltip("The STT client this harness drives. Assign it in the Inspector; if left empty it " +
                 "is resolved from the scene on Play.")]
        [SerializeField] private ElevenLabsScribeClient _scribeClient;

        [Header("Microphone (session)")]
        [Tooltip("MASTER microphone switch. ON opens the persistent STT session (WebSocket) and keeps " +
                 "it open; OFF closes it. Push-to-talk only captures utterances while this is ON. " +
                 "Safe to toggle at runtime in the Inspector.")]
        [SerializeField] private bool _microphoneEnabled;

        [Header("Capture Mode")]
        [Tooltip("Push-To-Talk: hold to capture, release to send. Voice-Activity: press to arm, " +
                 "auto-stops on silence. Capture only works while the Microphone toggle is ON.")]
        [SerializeField] private CaptureMode _captureMode = CaptureMode.PushToTalk;

        [Header("Controls (new Input System)")]
        [Tooltip("Display label for the push-to-talk control (the real binding lives on the " +
                 "InputSystem_Actions 'PushToTalk' action).")]
        [SerializeField] private string _controlBinding = "PushToTalk";

        [Header("Voice-Activity Tuning")]
        [Tooltip("Peak amplitude (0-1) below which audio is treated as silence. Lower it if the " +
                 "input meter reads low while you speak. Applied to the STT client on each capture.")]
        [SerializeField, Range(0f, 0.5f)] private float _vadAmplitudeThreshold = 0.02f;

        [Tooltip("Seconds of continuous silence after speech before the utterance is finalized.")]
        [SerializeField, Min(0.1f)] private float _vadSilenceSeconds = 1.2f;

        [Tooltip("After a session drop, wait this long before auto-reconnecting (voice-activity), so " +
                 "a failing reconnect backs off instead of hammering every frame.")]
        [SerializeField, Min(0f)] private float _reconnectCooldownSeconds = 2f;

        [Header("Debug Log File")]
        [Tooltip("Write transcripts + status/errors to a UTF-8 text file so the Arabic/Egyptian output " +
                 "can be reviewed outside the Unity Console (which can't render Arabic). The file is " +
                 "saved under Application.persistentDataPath and appended to across sessions.")]
        [SerializeField] private bool _logToFile = true;

        [Tooltip("Name of the debug log file written (UTF-8, appended) under Application.persistentDataPath.")]
        [SerializeField] private string _logFileName = "stt_debug_log.txt";

        private InputSystem_Actions _inputSystem;
        private bool _microphoneToggleApplied;   // tracks the last-applied value of _microphoneEnabled
        private bool _lastMicrophoneEnabled;
        private bool _pushToTalkHeld;             // true while the PTT control is held down
        private bool _startedByHarness;           // did WE start the current capture (so release may stop it)
        private float _reconnectAllowedTime;      // earliest time we may auto-reconnect after a drop
        private string _status = "Idle.";
        private string _lastTranscript = "";
        private string _lastError = "";
        private string _logFilePath;             // resolved once in Start when _logToFile is on
        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;

        private void Awake()
        {
            _inputSystem = new InputSystem_Actions();
            _inputSystem.Player.PushToTalk.started += OnTalkPressed;    // control down
            _inputSystem.Player.PushToTalk.canceled += OnTalkReleased;  // control up
        }

        private void Start()
        {
            if (_disableMerchantTest)
            {
                var drivers = FindObjectsByType<MerchantSystemTestDriver>(FindObjectsSortMode.None);
                foreach (var driver in drivers)
                {
                    driver.enabled = false;
                    Debug.Log($"[SttTest] Disabled MerchantSystemTestDriver on '{driver.name}' for isolated STT test.");
                }
            }

            if (_grantConsentOnPlay)
            {
                if (ConsentManager.Singleton != null)
                {
                    ConsentManager.Singleton.SubmitConsentResult(ConsentStatus.Granted, ConsentStatus.Granted);
                    Debug.Log("[SttTest] Consent granted (mic + internet).");
                }
                else
                {
                    Debug.LogWarning("[SttTest] No ConsentManager in scene — STT will fail its consent gate.");
                }
            }

            // Resolve the STT client: prefer the Inspector-assigned reference, else find it in scene.
            if (_scribeClient == null)
                _scribeClient = FindFirstObjectByType<ElevenLabsScribeClient>();

            if (_scribeClient != null)
            {
                _scribeClient.OnTranscriptionComplete += HandleComplete;
                _scribeClient.OnTranscriptionFailed += HandleFailed;
            }
            else
            {
                Debug.LogWarning("[SttTest] No ElevenLabsScribeClient assigned or found in scene.");
            }

            InitLogFile();

            // Apply the initial microphone toggle (opens the session if it starts enabled).
            _lastMicrophoneEnabled = _microphoneEnabled;
            _microphoneToggleApplied = true;
            if (_scribeClient != null) ApplyMicrophoneToggle(_scribeClient);
            else _status = $"{_captureMode}: no STT client.";
        }

        // --- Debug log file (UTF-8 so Arabic renders in a normal text editor) -------

        // Resolve the log path and write a session header. UTF-8 (via Encoding.UTF8) means the BOM
        // is emitted only when the file is first created, so any text editor detects the encoding and
        // renders Arabic/Egyptian transcripts correctly — unlike the Unity Console.
        private void InitLogFile()
        {
            if (!_logToFile) return;

            if (string.IsNullOrWhiteSpace(_logFileName)) _logFileName = "stt_debug_log.txt";
            try
            {
                _logFilePath = Path.Combine(Application.persistentDataPath, _logFileName);
                File.AppendAllText(_logFilePath,
                    $"{Environment.NewLine}===== STT session {DateTime.Now:yyyy-MM-dd HH:mm:ss} " +
                    $"(mode: {_captureMode}) ====={Environment.NewLine}",
                    Encoding.UTF8);
                Debug.Log($"[SttTest] Debug log → {_logFilePath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SttTest] Could not open debug log file: {ex.Message}");
                _logFilePath = null;
            }
        }

        // Append one timestamped, labelled line. No-op when file logging is off or the file failed
        // to open, so callers never have to guard.
        private void WriteLog(string label, string message)
        {
            if (!_logToFile || string.IsNullOrEmpty(_logFilePath)) return;
            try
            {
                File.AppendAllText(_logFilePath,
                    $"[{DateTime.Now:HH:mm:ss}] {label}: {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SttTest] Failed to write debug log: {ex.Message}");
            }
        }

        private void OnEnable()
        {
            _inputSystem?.Enable();
        }

        private void OnDisable()
        {
            _inputSystem?.Disable();
        }

        private void OnDestroy()
        {
            if (_scribeClient != null)
            {
                _scribeClient.OnTranscriptionComplete -= HandleComplete;
                _scribeClient.OnTranscriptionFailed -= HandleFailed;
                _scribeClient.CloseSession();
            }
        }

        private void Update()
        {
            var client = _scribeClient;
            if (client == null) return;

            // 1) Apply the serialized MICROPHONE (session) toggle whenever it changes.
            if (!_microphoneToggleApplied || _microphoneEnabled != _lastMicrophoneEnabled)
            {
                _lastMicrophoneEnabled = _microphoneEnabled;
                _microphoneToggleApplied = true;
                ApplyMicrophoneToggle(client);
            }

            // 2) Push-to-talk drives CAPTURE only, and only while the microphone (session) is ON.
            if (_captureMode == CaptureMode.VoiceActivity)
            {
                // Arm once; after a session drop, re-arm (reconnect) only past the cooldown.
                if (!client.MicrophoneEnabled && _microphoneEnabled
                    && Time.unscaledTime >= _reconnectAllowedTime)
                    BeginCapture(client);
                return;
            }

            bool held = _pushToTalkHeld && _microphoneEnabled;
            bool capturing = client.MicrophoneEnabled;

            if (held && !capturing && !_startedByHarness)
            {
                BeginCapture(client);
            }
            else if (!held && _startedByHarness)
            {
                _startedByHarness = false;
                Debug.Log("[SttTest] PTT released → stop capture (commit + playback).");
                _status = "Processing… awaiting transcript.";
                client.SetMicrophoneEnabled(false);   // stop capturing this utterance
            }
        }

        // Microphone bool → persistent session lifecycle.
        private void ApplyMicrophoneToggle(ElevenLabsScribeClient client)
        {
            if (_microphoneEnabled)
            {
                _status = "Microphone ON — session open. Use push-to-talk to capture.";
                client.OpenSession();
            }
            else
            {
                _status = "Microphone OFF — session closed.";
                _startedByHarness = false;
                client.CloseSession();
            }
        }

        // Voice-activity: a press ARMS capture; the client stops itself on silence.
        private void OnTalkPressed(InputAction.CallbackContext _)
        {
            _pushToTalkHeld = true;

            if (_captureMode != CaptureMode.VoiceActivity) return;   // PTT handled in Update()

            var client = _scribeClient;
            if (client == null) return;
            if (!_microphoneEnabled)
            {
                _status = "Enable the Microphone toggle first.";
                return;
            }
            if (client.MicrophoneEnabled) return;   // already capturing
            BeginCapture(client);
        }

        private void OnTalkReleased(InputAction.CallbackContext _)
        {
            _pushToTalkHeld = false;
        }

        private void BeginCapture(ElevenLabsScribeClient client)
        {
            ConfigureClientForMode(client);
            _lastError = "";
            _lastTranscript = "";
            _startedByHarness = true;
            _status = _captureMode == CaptureMode.PushToTalk
                ? "Capturing… release to send."
                : "Armed — speak; auto-stops on silence.";
            client.SetMicrophoneEnabled(true);   // start capturing an utterance in the open session
        }

        // Point the STT client at the right stop behaviour for the selected capture mode.
        private void ConfigureClientForMode(ElevenLabsScribeClient client)
        {
            if (_captureMode == CaptureMode.PushToTalk)
            {
                // Not voice-gated: the full held audio streams; release commits.
                client.AutoStopOnSilence = false;
            }
            else
            {
                // Voice-gated: only the player's actual voice streams; silence commits and the mic
                // stays open (armed) for the next utterance.
                client.AutoStopOnSilence = true;
                client.SilenceAmplitudeThreshold = _vadAmplitudeThreshold;
                client.SilenceThresholdSeconds = _vadSilenceSeconds;
            }
        }

        private void HandleComplete(string text)
        {
            _startedByHarness = false;
            _lastTranscript = text ?? "";
            _status = $"Transcript received ({_lastTranscript.Length} chars). Session stays open.";
            Debug.Log($"[SttTest] Transcript: \"{_lastTranscript}\"");
            // Write the actual text to the UTF-8 log — the Console can't render the Arabic result.
            WriteLog($"TRANSCRIPT ({_captureMode}, {_lastTranscript.Length} chars)", _lastTranscript);
        }

        private void HandleFailed(string reason)
        {
            _startedByHarness = false;
            _reconnectAllowedTime = Time.unscaledTime + _reconnectCooldownSeconds;
            _lastError = reason;
            _status = $"Failed: {reason}";
            Debug.LogWarning($"[SttTest] STT failed: {reason}");
            WriteLog("ERROR", reason);
        }

        private void OnGUI()
        {
            _boxStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 10, 10) };
            _labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, richText = true };

            const float w = 480f;
            GUILayout.BeginArea(new Rect(10, 10, w, 400), _boxStyle);

            string hint = _captureMode == CaptureMode.PushToTalk
                ? $"hold '{_controlBinding}'"
                : $"press '{_controlBinding}' to arm";
            GUILayout.Label($"<b>Voice → STT Test — {_captureMode}</b>  ({hint})", _labelStyle);
            GUILayout.Space(4);

            // Microphone toggle (serialized) + actual session/capture state on the client.
            var client = _scribeClient;
            string micToggle = _microphoneEnabled ? "<color=#80ff80>ON</color>" : "<color=#ffcc80>OFF</color>";
            string session = client == null ? "no client"
                : client.IsSessionOpen ? "<color=#80ff80>open</color>" : "closed";
            string capturing = client != null && client.MicrophoneEnabled
                ? "<color=#80ff80>yes</color>" : "no";
            GUILayout.Label($"Microphone: {micToggle}   Session: {session}   Capturing: {capturing}", _labelStyle);
            GUILayout.Label($"Status: {_status}", _labelStyle);

            // Live input-level meter vs the silence threshold (meaningful while capturing).
            float level = client != null ? client.CurrentInputLevel : 0f;
            GUILayout.Label($"Input level: {level:F3}   (VAD threshold: {_vadAmplitudeThreshold:F3})", _labelStyle);
            Rect bar = GUILayoutUtility.GetRect(w - 20, 16);
            GUI.Box(bar, GUIContent.none);
            GUI.Box(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(level * 4f), bar.height), GUIContent.none);

            GUILayout.Space(6);
            GUILayout.Label($"<b>Transcript:</b> {_lastTranscript}", _labelStyle);
            if (!string.IsNullOrEmpty(_lastError))
                GUILayout.Label($"<color=#ff8080><b>Error:</b> {_lastError}</color>", _labelStyle);

            // Debug log file: show where transcripts are saved and offer a shortcut to the folder,
            // since Arabic output has to be read there rather than in the Console.
            if (_logToFile && !string.IsNullOrEmpty(_logFilePath))
            {
                GUILayout.Space(6);
                GUILayout.Label($"<size=11>Log → {_logFilePath}</size>", _labelStyle);
                if (GUILayout.Button("Open log folder"))
                    Application.OpenURL("file://" + Application.persistentDataPath);
            }

            GUILayout.EndArea();
        }
    }
}
