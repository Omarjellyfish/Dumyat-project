using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Presenter-only notes, toggled with N. Hidden by default and never part of the composition —
    /// it exists so the notes can live on the slide asset instead of on paper.
    /// </summary>
    public sealed class SpeakerNotesOverlay : MonoBehaviour
    {
        [SerializeField] private PresentationView _view;

        private IEventService _events;
        private bool _visible;
        private string _pending = string.Empty;

        [Inject]
        private void Inject(IEventService events) => _events = events;

        private void Awake()
        {
            if (_view == null) _view = GetComponentInParent<PresentationView>();
        }

        private void OnEnable()
        {
            _events?.Subscribe<SlideChangedEvent>(OnSlideChanged);
            Apply();
        }

        private void OnDisable() => _events?.Unsubscribe<SlideChangedEvent>(OnSlideChanged);

        public void Toggle()
        {
            _visible = !_visible;
            Apply();
        }

        private void OnSlideChanged(SlideChangedEvent e)
        {
            _pending = e.Slide != null ? e.Slide.SpeakerNotes ?? string.Empty : string.Empty;
            Apply();
        }

        private void Apply()
        {
            if (_view == null || !_view.IsReady) return;

            _view.NotesText.text = _pending;

            // A slide with no notes shows nothing even when toggled on, rather than an empty box.
            bool show = _visible && !string.IsNullOrWhiteSpace(_pending);
            _view.NotesPanel.EnableInClassList("visible", show);
        }
    }
}
