using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// TEST-ONLY: isolates the voice → Speech-to-Text path from the merchant negotiation pipeline so
    /// mic recording quality and the returned transcript can be judged on their own. The actual
    /// capture logic now lives in the shared <see cref="VoiceCaptureController"/> (used by the real
    /// negotiation too); this harness is a thin driver + on-screen visualizer that forwards its
    /// Inspector settings to that controller and draws a live status/meter via OnGUI.
    ///
    /// TWO LEVELS OF CONTROL:
    ///   • MICROPHONE — a serialized boolean (<see cref="_microphoneEnabled"/>). Toggling it ON opens
    ///     the persistent STT session (and, in continuous mode, arms capture); OFF closes it. Safe to
    ///     flip in the Inspector at runtime.
    ///   • CAPTURE MODE / TUNING — push-to-talk vs voice-activity, release tail, VAD thresholds,
    ///     reconnect cooldown, and file logging, all forwarded to the controller.
    ///
    /// It also grants AI consent and disables any <see cref="MerchantSystemTestDriver"/> so nothing
    /// else contends for the mic. Not part of the shipping game.
    /// </summary>
    public class SttVoiceTestHarness : MonoBehaviour
    {
        // Injected consent service (replaces the former ConsentManager.Singleton).
        private IConsentService _consent;

        [Inject]
        private void Inject(IConsentService consent) => _consent = consent;

        [Header("Setup")]
        [Tooltip("Auto-grant mic + internet consent on Play so the STT path is reachable.")]
        [SerializeField] private bool _grantConsentOnPlay = true;

        [Tooltip("Disable any MerchantSystemTestDriver in the scene so the negotiation test does " +
                 "not also start and contend for the microphone / STT source.")]
        [SerializeField] private bool _disableMerchantTest = true;

        [Tooltip("The STT client this harness drives. Assign it in the Inspector; if left empty it " +
                 "is resolved from the scene on Play.")]
        [SerializeField] private ElevenLabsScribeClient _scribeClient;

        [Tooltip("Shared capture controller. Assign one, or leave empty to auto-add it to this object.")]
        [SerializeField] private VoiceCaptureController _voiceCapture;

        [Header("Microphone (session)")]
        [Tooltip("MASTER microphone switch. ON opens the persistent STT session (and, in continuous " +
                 "mode, arms capture); OFF closes it. Safe to toggle at runtime in the Inspector.")]
        [SerializeField] private bool _microphoneEnabled;

        [Header("Capture Mode")]
        [Tooltip("Push-To-Talk: hold to capture, release to send. Voice-Activity: auto-captures speech " +
                 "and commits on silence.")]
        [SerializeField] private VoiceCaptureController.CaptureMode _captureMode =
            VoiceCaptureController.CaptureMode.VoiceActivity;

        [Header("Controls (new Input System)")]
        [Tooltip("Display label for the push-to-talk control (the real binding lives on the " +
                 "InputSystem_Actions 'PushToTalk' action).")]
        [SerializeField] private string _controlBinding = "PushToTalk";

        [Tooltip("Push-to-talk only: after releasing the button, keep capturing for this many seconds " +
                 "so the trailing audio of the last word is included. 0 = stop immediately on release.")]
        [SerializeField, Min(0f)] private float _pttReleaseTailSeconds = 0.35f;

        [Header("Voice-Activity Tuning")]
        [Tooltip("Peak amplitude (0-1) below which audio is treated as silence. Lower it if the input " +
                 "meter reads low while you speak.")]
        [SerializeField, Range(0f, 0.5f)] private float _vadAmplitudeThreshold = 0.02f;

        [Tooltip("Seconds of continuous silence after speech before the utterance is finalized.")]
        [SerializeField, Min(0.1f)] private float _vadSilenceSeconds = 1.2f;

        [Tooltip("After a session drop, wait this long before auto-reconnecting (voice-activity).")]
        [SerializeField, Min(0f)] private float _reconnectCooldownSeconds = 2f;

        [Header("Debug Log File")]
        [Tooltip("Write transcripts + status/errors to a UTF-8 text file so the Arabic/Egyptian output " +
                 "can be reviewed outside the Unity Console. Saved under Application.persistentDataPath.")]
        [SerializeField] private bool _logToFile = true;

        [Tooltip("Name of the debug log file written (UTF-8, appended) under Application.persistentDataPath.")]
        [SerializeField] private string _logFileName = "stt_debug_log.txt";

        private bool _microphoneToggleApplied;   // tracks the last-applied value of _microphoneEnabled
        private bool _lastMicrophoneEnabled;
        private string _lastTranscript = "";
        private string _lastError = "";
        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;

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
                if (_consent != null)
                {
                    _consent.SubmitConsentResult(ConsentStatus.Granted, ConsentStatus.Granted);
                    Debug.Log("[SttTest] Consent granted (mic + internet).");
                }
                else
                {
                    Debug.LogWarning("[SttTest] No IConsentService injected — STT will fail its consent gate.");
                }
            }

            // Resolve the STT client (prefer the Inspector-assigned reference, else find it in scene).
            if (_scribeClient == null)
                _scribeClient = FindFirstObjectByType<ElevenLabsScribeClient>();

            // Ensure a capture controller and hand it the source. The harness listens continuously.
            if (_voiceCapture == null)
                _voiceCapture = gameObject.AddComponent<VoiceCaptureController>();

            _voiceCapture.AutoReArm = true;
            _voiceCapture.LogFileName = _logFileName;   // must be set before the first log line
            if (_scribeClient != null) _voiceCapture.AssignSource(_scribeClient);
            else Debug.LogWarning("[SttTest] No ElevenLabsScribeClient assigned or found in scene.");

            _voiceCapture.OnTranscription += HandleTranscription;
            _voiceCapture.OnCaptureFailed += HandleFailed;

            PushConfig();

            // Apply the initial microphone toggle (opens the session if it starts enabled).
            _lastMicrophoneEnabled = _microphoneEnabled;
            _microphoneToggleApplied = true;
            ApplyMicrophoneToggle();
        }

        private void OnDestroy()
        {
            if (_voiceCapture != null)
            {
                _voiceCapture.OnTranscription -= HandleTranscription;
                _voiceCapture.OnCaptureFailed -= HandleFailed;
                _voiceCapture.CloseSession();
            }
        }

        private void Update()
        {
            if (_voiceCapture == null) return;

            // Keep the controller in sync with the Inspector (so runtime tweaks apply live).
            PushConfig();

            // Apply the serialized MICROPHONE (session) toggle whenever it changes.
            if (!_microphoneToggleApplied || _microphoneEnabled != _lastMicrophoneEnabled)
            {
                _lastMicrophoneEnabled = _microphoneEnabled;
                _microphoneToggleApplied = true;
                ApplyMicrophoneToggle();
            }
        }

        // Forward the harness's Inspector settings onto the shared controller.
        private void PushConfig()
        {
            _voiceCapture.Mode = _captureMode;
            _voiceCapture.ControlBinding = _controlBinding;
            _voiceCapture.ReleaseTailSeconds = _pttReleaseTailSeconds;
            _voiceCapture.VadAmplitudeThreshold = _vadAmplitudeThreshold;
            _voiceCapture.VadSilenceSeconds = _vadSilenceSeconds;
            _voiceCapture.ReconnectCooldownSeconds = _reconnectCooldownSeconds;
            _voiceCapture.LogToFile = _logToFile;
        }

        // Microphone bool → persistent session lifecycle on the controller.
        private void ApplyMicrophoneToggle()
        {
            if (_microphoneEnabled) _voiceCapture.OpenSession();
            else _voiceCapture.CloseSession();
        }

        private void HandleTranscription(string text)
        {
            _lastTranscript = text ?? "";
            Debug.Log($"[SttTest] Transcript: \"{_lastTranscript}\"");
        }

        private void HandleFailed(string reason)
        {
            _lastError = reason;
            Debug.LogWarning($"[SttTest] STT failed: {reason}");
        }

        private void OnGUI()
        {
            _boxStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 10, 10) };
            _labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, richText = true };

            const float w = 480f;
            GUILayout.BeginArea(new Rect(10, 10, w, 400), _boxStyle);

            string hint = _captureMode == VoiceCaptureController.CaptureMode.PushToTalk
                ? $"hold '{_controlBinding}'"
                : $"press '{_controlBinding}' to arm";
            GUILayout.Label($"<b>Voice → STT Test — {_captureMode}</b>  ({hint})", _labelStyle);
            GUILayout.Space(4);

            var vc = _voiceCapture;
            string micToggle = _microphoneEnabled ? "<color=#80ff80>ON</color>" : "<color=#ffcc80>OFF</color>";
            string session = vc == null ? "no controller"
                : vc.IsSessionOpen ? "<color=#80ff80>open</color>" : "closed";
            string capturing = vc != null && vc.IsCapturing ? "<color=#80ff80>yes</color>" : "no";
            GUILayout.Label($"Microphone: {micToggle}   Session: {session}   Capturing: {capturing}", _labelStyle);
            GUILayout.Label($"Status: {(vc != null ? vc.StatusText : "-")}", _labelStyle);

            // Live input-level meter vs the silence threshold (meaningful while capturing).
            float level = vc != null ? vc.CurrentInputLevel : 0f;
            GUILayout.Label($"Input level: {level:F3}   (VAD threshold: {_vadAmplitudeThreshold:F3})", _labelStyle);
            Rect bar = GUILayoutUtility.GetRect(w - 20, 16);
            GUI.Box(bar, GUIContent.none);
            GUI.Box(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(level * 4f), bar.height), GUIContent.none);

            GUILayout.Space(6);
            GUILayout.Label($"<b>Transcript:</b> {_lastTranscript}", _labelStyle);
            if (!string.IsNullOrEmpty(_lastError))
                GUILayout.Label($"<color=#ff8080><b>Error:</b> {_lastError}</color>", _labelStyle);

            string logPath = vc != null ? vc.LogFilePath : null;
            if (_logToFile && !string.IsNullOrEmpty(logPath))
            {
                GUILayout.Space(6);
                GUILayout.Label($"<size=11>Log → {logPath}</size>", _labelStyle);
                if (GUILayout.Button("Open log folder"))
                    Application.OpenURL("file://" + Application.persistentDataPath);
            }

            GUILayout.EndArea();
        }
    }
}
