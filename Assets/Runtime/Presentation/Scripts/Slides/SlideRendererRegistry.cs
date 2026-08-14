using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Maps a slide asset to the renderer that handles it, and guarantees only one renderer is
    /// showing at a time.
    ///
    /// The director asks this rather than knowing any slide type, so adding the media, scene, UI and
    /// turntable renderers in later phases means dropping a component in and listing it here — the
    /// director never changes again.
    /// </summary>
    public sealed class SlideRendererRegistry : MonoBehaviour
    {
        [SerializeField, Tooltip("Components implementing ISlideRenderer, in priority order. The " +
                                 "first one whose CanRender returns true wins.")]
        private MonoBehaviour[] _renderers = new MonoBehaviour[0];

        private readonly List<ISlideRenderer> _resolved = new List<ISlideRenderer>();
        private ISlideRenderer _active;

        private void Awake()
        {
            foreach (MonoBehaviour candidate in _renderers)
            {
                if (candidate == null) continue;
                if (candidate is ISlideRenderer renderer) _resolved.Add(renderer);
                else
                    Debug.LogWarning($"[SlideRendererRegistry] '{candidate.GetType().Name}' does not " +
                                     "implement ISlideRenderer and was skipped.", this);
            }

            // Auto-discover on this rig if nothing was wired, so a fresh scene works out of the box.
            if (_resolved.Count == 0)
            {
                foreach (ISlideRenderer found in GetComponentsInChildren<ISlideRenderer>(true))
                    _resolved.Add(found);
            }

            if (_resolved.Count == 0)
                Debug.LogWarning("[SlideRendererRegistry] no renderers registered — no slide will draw.", this);
        }

        /// <summary>
        /// Shows <paramref name="slide"/> with whichever renderer claims it, hiding the previous one
        /// first. Returns false when nothing can render it, so the director can log the gap rather
        /// than leave the last slide on screen and look like it froze.
        /// </summary>
        public bool Show(SlideSO slide, PresentationDeckSO deck)
        {
            if (slide == null) return false;

            ISlideRenderer next = null;
            foreach (ISlideRenderer candidate in _resolved)
            {
                if (!candidate.CanRender(slide)) continue;
                next = candidate;
                break;
            }

            if (next == null)
            {
                Debug.LogWarning($"[SlideRendererRegistry] no renderer for '{slide.name}' " +
                                 $"({slide.GetType().Name}).", this);
                return false;
            }

            if (_active != null && !ReferenceEquals(_active, next)) _active.Hide();
            _active = next;
            next.Show(slide, deck);
            return true;
        }

        public void HideAll()
        {
            foreach (ISlideRenderer r in _resolved) r.Hide();
            _active = null;
        }
    }
}
