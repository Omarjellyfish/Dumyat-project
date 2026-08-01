using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Shows the player's OWN live speech as a caption while they talk, driven by
    /// <see cref="PlayerPartialTranscriptEvent"/> (published by <see cref="NegotiationFlowController"/> only
    /// when its "Show Player Partials" toggle is on). Purely a real-time readout of what the STT is hearing
    /// — it never affects the negotiation, which always decides on the FINAL transcript.
    ///
    /// Drop this on a GameObject that carries (or parents) a <see cref="SubtitlePanel"/> placed in front of
    /// the player (e.g. under the XR rig / camera). Mirrors <see cref="MerchantSubtitlePresenter"/> but with
    /// a single speaker (the player), so it needs no merchant filter.
    ///
    /// NOTE: partials are the player's words in the capture language (Egyptian Arabic). The shared subtitle
    /// font renders English only, so this panel needs an Arabic-capable TMP font/fallback or the caption
    /// shows missing-glyph boxes. Leave the feature off (the controller toggle) if no Arabic font is set.
    /// </summary>
    public class PlayerCaptionPresenter : MonoBehaviour
    {
        [Tooltip("Panel that displays the player's live words. Auto-resolved from children if empty.")]
        [SerializeField] private SubtitlePanel _panel;

        [Tooltip("Speaker label shown above the caption (e.g. \"YOU\"). Empty = no label.")]
        [SerializeField] private string _speakerLabel = "You";

        private IEventService _eventService;

        [Inject]
        private void Inject(IEventService eventService) => _eventService = eventService;

        private void Awake()
        {
            if (_panel == null) _panel = GetComponentInChildren<SubtitlePanel>(true);

            if (_panel == null)
                Debug.LogWarning($"{nameof(PlayerCaptionPresenter)} on '{name}' has no SubtitlePanel in its " +
                                 "children — the player caption will not show.", this);
            else
                _panel.Hide();   // nothing to say yet
        }

        private void OnEnable()
        {
            if (_eventService == null)
            {
                Debug.LogWarning($"{nameof(PlayerCaptionPresenter)} on '{name}' has no IEventService " +
                                 "injected — the player caption will not appear. Does this scene have a " +
                                 "ContainerScope?", this);
                return;
            }
            _eventService.Subscribe<PlayerPartialTranscriptEvent>(HandlePartial);
        }

        private void OnDisable()
        {
            _eventService?.Unsubscribe<PlayerPartialTranscriptEvent>(HandlePartial);
        }

        private void HandlePartial(PlayerPartialTranscriptEvent evt)
        {
            if (_panel == null) return;

            // Empty text is the controller's signal to clear the caption (final arrived / turn ended).
            if (string.IsNullOrWhiteSpace(evt.Text)) { _panel.Hide(); return; }

            // Partials update many times a second, so show the whole line at once — a type-out would never
            // catch up and would jitter as each partial replaces the last.
            _panel.ShowInstant(evt.Text, string.IsNullOrWhiteSpace(_speakerLabel) ? null : _speakerLabel);
        }
    }
}
