using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The "27 / 58" readout, the constant footer string, and the progress rail along the bottom edge.
    ///
    /// Reacts to <see cref="SlideChangedEvent"/> rather than holding the director, which is why it can
    /// be deleted from the rig without the deck noticing.
    /// </summary>
    public sealed class SlideCounterHud : MonoBehaviour
    {
        [SerializeField] private PresentationView _view;
        [SerializeField] private PresentationDeckSO _deck;

        [SerializeField, Tooltip("Start with the counter showing. C toggles it during a talk.")]
        private bool _visibleAtStart = true;

        private IEventService _events;
        private bool _visible;

        [Inject]
        private void Inject(IEventService events) => _events = events;

        private void Awake()
        {
            if (_view == null) _view = GetComponentInParent<PresentationView>();
            _visible = _visibleAtStart;
        }

        private void OnEnable()
        {
            // Cached method group, not a lambda, so Unsubscribe matches — the project convention.
            _events?.Subscribe<SlideChangedEvent>(OnSlideChanged);
            ApplyVisibility();
            ApplyFooterLeft();
        }

        private void OnDisable() => _events?.Unsubscribe<SlideChangedEvent>(OnSlideChanged);

        public void Toggle()
        {
            _visible = !_visible;
            ApplyVisibility();
        }

        private void OnSlideChanged(SlideChangedEvent e)
        {
            if (_view == null || !_view.IsReady) return;

            _view.FooterCounter.text = $"{e.Index + 1} / {e.Count}";

            // The rail encodes deck position with no numbers, so it stays useful when the counter is off.
            float progress = e.Count > 1 ? (e.Index + 1) / (float)e.Count : 1f;
            _view.ProgressFill.style.width = new UnityEngine.UIElements.Length(
                progress * 100f, UnityEngine.UIElements.LengthUnit.Percent);

            if (_deck?.Theme != null)
            {
                _view.ProgressFill.style.backgroundColor = _deck.Theme.Accent;
                _view.FooterCounter.style.color = _deck.Theme.InkMuted;
                _view.FooterLeft.style.color = _deck.Theme.InkMuted;
            }
        }

        private void ApplyFooterLeft()
        {
            if (_view == null || !_view.IsReady) return;
            _view.FooterLeft.text = _deck?.Theme != null ? _deck.Theme.FooterLeft : string.Empty;
        }

        private void ApplyVisibility()
        {
            if (_view == null || !_view.IsReady) return;
            _view.FooterCounter.EnableInClassList("hidden", !_visible);
        }
    }
}
