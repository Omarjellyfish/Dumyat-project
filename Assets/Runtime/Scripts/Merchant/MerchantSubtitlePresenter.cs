using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Shows one merchant's dialogue on its own <see cref="SubtitlePanel"/>. Sits on the merchant next
    /// to its <see cref="MerchantNPC"/> and is the only piece that knows about both, which keeps
    /// <see cref="SubtitlePanel"/> reusable for any speaker and <see cref="MerchantNPC"/> free of UI.
    ///
    /// Drop this plus a SubtitlePanel child onto any merchant that should show subtitles; merchants
    /// without one simply stay silent.
    /// </summary>
    [RequireComponent(typeof(MerchantNPC))]
    public class MerchantSubtitlePresenter : MonoBehaviour
    {
        [Tooltip("The merchant whose lines this shows. Auto-resolved from this GameObject if empty.")]
        [SerializeField] private MerchantNPC _merchant;

        [Tooltip("Panel that displays the lines. Auto-resolved from children if empty.")]
        [SerializeField] private SubtitlePanel _panel;

        private IEventService _eventService;
        private ISettingsService _settings;

        // The last line spoken, kept so a mid-line language change can re-render it rather than
        // leaving the player staring at the previous language until the merchant speaks again.
        private MerchantSpokeEvent _lastLine;
        private bool _hasLine;

        [Inject]
        private void Inject(IEventService eventService, ISettingsService settings)
        {
            _eventService = eventService;
            _settings = settings;
        }

        private void Awake()
        {
            if (_merchant == null) _merchant = GetComponent<MerchantNPC>();
            if (_panel == null) _panel = GetComponentInChildren<SubtitlePanel>(true);

            if (_panel == null)
                Debug.LogWarning($"{nameof(MerchantSubtitlePresenter)} on '{name}' has no SubtitlePanel " +
                                 "in its children — this merchant will show no subtitles.", this);
            else
                _panel.Hide();   // nothing to say yet
        }

        private void OnEnable()
        {
            if (_eventService == null)
            {
                Debug.LogWarning($"{nameof(MerchantSubtitlePresenter)} on '{name}' has no IEventService " +
                                 "injected — subtitles will not appear. Does this scene have a " +
                                 "ContainerScope?", this);
                return;
            }

            _eventService.Subscribe<MerchantSpokeEvent>(HandleMerchantSpoke);
            _eventService.Subscribe<MerchantStoppedSpeakingEvent>(HandleMerchantStopped);
            _eventService.Subscribe<SubtitleModeChangedEvent>(HandleSubtitleModeChanged);
        }

        private void OnDisable()
        {
            if (_eventService == null) return;
            _eventService.Unsubscribe<MerchantSpokeEvent>(HandleMerchantSpoke);
            _eventService.Unsubscribe<MerchantStoppedSpeakingEvent>(HandleMerchantStopped);
            _eventService.Unsubscribe<SubtitleModeChangedEvent>(HandleSubtitleModeChanged);
        }

        // Every merchant publishes on the same channel, so each presenter takes only its own lines.
        private void HandleMerchantSpoke(MerchantSpokeEvent evt)
        {
            if (evt.Merchant != _merchant || _panel == null) return;

            _lastLine = evt;
            _hasLine = true;
            Render(evt, forceInstant: false);
        }

        private void HandleMerchantStopped(MerchantStoppedSpeakingEvent evt)
        {
            if (evt.Merchant != _merchant || _panel == null) return;

            _hasLine = false;
            _panel.Hide();
        }

        /// <summary>
        /// Re-renders the current line in the newly chosen language. Deliberately instant: the
        /// merchant's voice is already partway through the line, so restarting the type-out would
        /// put the caption behind the audio.
        /// </summary>
        private void HandleSubtitleModeChanged(SubtitleModeChangedEvent evt)
        {
            if (_panel == null) return;

            if (evt.Mode == SubtitleMode.Off) { _panel.Hide(); return; }
            if (_hasLine) Render(_lastLine, forceInstant: true);
        }

        private void Render(MerchantSpokeEvent evt, bool forceInstant)
        {
            // No settings service (a scene without a ContainerScope) degrades to English rather
            // than to silence — a missing dependency should not look like a broken merchant.
            SubtitleMode mode = _settings?.SubtitleMode ?? SubtitleMode.English;

            if (mode == SubtitleMode.Off) { _panel.Hide(); return; }

            string line = mode == SubtitleMode.Arabic ? evt.Arabic : evt.English;

            // Arabic is authored per line and can legitimately be missing on a generated one.
            // Falling back beats showing an empty panel while the merchant talks.
            if (string.IsNullOrWhiteSpace(line)) line = evt.English;

            string speaker = _merchant.Config != null ? _merchant.Config.MerchantName : null;

            if (evt.Instant || forceInstant) _panel.ShowInstant(line, speaker);   // no voice to pace the type-out
            else _panel.Show(line, speaker);
        }
    }
}
