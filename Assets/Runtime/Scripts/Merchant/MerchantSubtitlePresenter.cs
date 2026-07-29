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

        [Inject]
        private void Inject(IEventService eventService) => _eventService = eventService;

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
        }

        private void OnDisable()
        {
            if (_eventService == null) return;
            _eventService.Unsubscribe<MerchantSpokeEvent>(HandleMerchantSpoke);
            _eventService.Unsubscribe<MerchantStoppedSpeakingEvent>(HandleMerchantStopped);
        }

        // Every merchant publishes on the same channel, so each presenter takes only its own lines.
        private void HandleMerchantSpoke(MerchantSpokeEvent evt)
        {
            if (evt.Merchant != _merchant || _panel == null) return;
            string speaker = _merchant.Config != null ? _merchant.Config.MerchantName : null;
            if (evt.Instant) _panel.ShowInstant(evt.English, speaker);   // no voice to pace the type-out
            else _panel.Show(evt.English, speaker);
        }

        private void HandleMerchantStopped(MerchantStoppedSpeakingEvent evt)
        {
            if (evt.Merchant != _merchant || _panel == null) return;
            _panel.Hide();
        }
    }
}
