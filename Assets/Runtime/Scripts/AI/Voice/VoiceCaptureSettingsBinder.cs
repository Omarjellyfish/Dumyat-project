using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Carries the player's microphone options from the settings page onto the
    /// <see cref="VoiceCaptureController"/> beside it, and keeps them in step while the game runs.
    ///
    /// A separate component rather than another responsibility on the controller: that class already
    /// owns session/capture lifecycle, mode switching, the release tail, the speech cap and the debug
    /// log, and it works perfectly well with nothing but its Inspector values. This is the one piece
    /// that knows a settings menu exists, so a scene that has no menu simply omits it.
    ///
    /// Settings live in the ROOT container and this lives in a scene, so the two are joined by an
    /// event rather than a reference (CLAUDE.md ▸ "Decouple via Reflex DI and EventBus"). It also
    /// pulls once on enable: <see cref="SettingsService"/> publishes its boot state during
    /// construction, long before any scene exists, so a binder that waited only for the event would
    /// sit on the Inspector defaults until the player next touched a control.
    /// </summary>
    [RequireComponent(typeof(VoiceCaptureController))]
    public sealed class VoiceCaptureSettingsBinder : MonoBehaviour
    {
        [Tooltip("Controller to configure. Auto-resolved from this GameObject when left empty.")]
        [SerializeField] private VoiceCaptureController _controller;

        private ISettingsService _settings;
        private IEventService _eventService;

        [Inject]
        private void Inject(ISettingsService settings, IEventService eventService)
        {
            _settings = settings;
            _eventService = eventService;
        }

        private void Awake()
        {
            if (_controller == null) _controller = GetComponent<VoiceCaptureController>();
        }

        private void OnEnable()
        {
            if (_controller == null)
            {
                Debug.LogWarning($"{nameof(VoiceCaptureSettingsBinder)} on '{name}' has no " +
                                 $"{nameof(VoiceCaptureController)} — the player's microphone options " +
                                 "will not reach voice capture.", this);
                return;
            }

            if (_settings == null)
            {
                Debug.LogWarning($"{nameof(VoiceCaptureSettingsBinder)} on '{name}' was not injected — " +
                                 "does this scene have a ContainerScope? Voice capture will run on its " +
                                 "Inspector defaults.", this);
                return;
            }

            _eventService?.Subscribe<VoiceSettingsChangedEvent>(OnVoiceSettingsChanged);

            Apply(_settings.MicCaptureMode, _settings.MicAutoSensitivity,
                  _settings.MicThreshold, _settings.PushToTalkButton, _settings.MicDevice);
        }

        private void OnDisable()
        {
            _eventService?.Unsubscribe<VoiceSettingsChangedEvent>(OnVoiceSettingsChanged);
        }

        private void OnVoiceSettingsChanged(VoiceSettingsChangedEvent evt)
        {
            Apply(evt.CaptureMode, evt.AutoSensitivity, evt.Threshold, evt.TalkButton, evt.Device);
        }

        /// <summary>
        /// Pushes every microphone option through the controller's public setters.
        ///
        /// The mode is mapped by hand, not cast: <see cref="MicCaptureMode"/> and
        /// <see cref="VoiceCaptureController.CaptureMode"/> declare their two members in OPPOSITE
        /// order, so a cast would compile and silently select the other mode.
        /// </summary>
        private void Apply(MicCaptureMode mode, bool autoSensitivity, float threshold,
                           PushToTalkButton talkButton, string device)
        {
            if (_controller == null) return;

            _controller.Mode = mode == MicCaptureMode.PushToTalk
                ? VoiceCaptureController.CaptureMode.PushToTalk
                : VoiceCaptureController.CaptureMode.VoiceActivity;

            _controller.AutoAdjustSensitivity = autoSensitivity;
            _controller.VadAmplitudeThreshold = threshold;
            _controller.PushToTalkButton = talkButton;

            // Empty is meaningful, not missing: it is "whatever the system offers first", which is
            // what MicrophoneStreamCapture.ResolveDevice falls back to. Pushed on every change so a
            // device switched in the menu takes effect on the NEXT utterance rather than needing the
            // negotiation restarted — the controller re-reads it in ConfigureSourceForMode.
            _controller.MicrophoneDevice = device ?? string.Empty;
        }
    }
}
