using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Runs the two halves of a transition and guarantees the screen is fully covered in between.
    ///
    /// WHY UNSCALED TIME: <c>GameStateService</c> sets <see cref="Time.timeScale"/> to 0 when the game
    /// enters its Paused state, and the root Reflex scope loads in this scene too. A deck that ran on
    /// scaled time could freeze mid-fade through no fault of its own.
    /// </summary>
    public sealed class SlideTransitionPlayer : MonoBehaviour
    {
        [SerializeField] private PresentationView _view;

        /// <summary>True from the first frame of PlayOut until the last frame of PlayIn.</summary>
        public bool IsPlaying { get; private set; }

        private void Awake()
        {
            if (_view == null) _view = GetComponentInParent<PresentationView>();
            if (_view == null)
            {
                Debug.LogWarning("[SlideTransitionPlayer] No PresentationView — transitions will be cuts.", this);
                return;
            }
            if (_view.IsReady) _view.Cover.style.opacity = 0f;
        }

        /// <summary>Covers the screen. Yield on this, then swap the slide, then yield on PlayIn.</summary>
        public IEnumerator PlayOut(SlideTransitionSO transition)
        {
            IsPlaying = true;

            if (_view == null || !_view.IsReady || transition == null ||
                transition.Kind == SlideTransitionKind.Cut)
                yield break;

            if (transition.Kind == SlideTransitionKind.CrossDissolve)
            {
                // Fades the content only, leaving the backdrop in place — text to text.
                yield return Fade(_view.Content, 1f, 0f, transition.OutDuration, transition);
                yield break;
            }

            _view.Cover.style.backgroundColor = transition.CoverColor;
            yield return Fade(_view.Cover, 0f, 1f, transition.OutDuration, transition);

            // Held fully covered. This is the window in which a stage swap happens unseen, and where
            // a newly activated scene gets a few frames to render before anybody sees it.
            if (transition.Hold > 0f)
            {
                float held = 0f;
                while (held < transition.Hold)
                {
                    held += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
        }

        /// <summary>Uncovers the screen.</summary>
        public IEnumerator PlayIn(SlideTransitionSO transition)
        {
            try
            {
                if (_view == null || !_view.IsReady || transition == null ||
                    transition.Kind == SlideTransitionKind.Cut)
                    yield break;

                if (transition.Kind == SlideTransitionKind.CrossDissolve)
                {
                    yield return Fade(_view.Content, 0f, 1f, transition.InDuration, transition);
                    yield break;
                }

                yield return Fade(_view.Cover, 1f, 0f, transition.InDuration, transition);
            }
            finally
            {
                // Whatever happened — including the coroutine being stopped — the deck must never be
                // left behind an opaque cover or with invisible content.
                if (_view != null && _view.IsReady)
                {
                    _view.Cover.style.opacity = 0f;
                    _view.Content.style.opacity = 1f;
                }
                IsPlaying = false;
            }
        }

        private static IEnumerator Fade(VisualElement element, float from, float to, float duration,
                                        SlideTransitionSO transition)
        {
            if (element == null) yield break;

            if (duration <= 0f)
            {
                element.style.opacity = to;
                yield break;
            }

            float elapsed = 0f;
            element.style.opacity = from;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = transition.Evaluate(Mathf.Clamp01(elapsed / duration));
                element.style.opacity = Mathf.LerpUnclamped(from, to, k);
                yield return null;
            }
            element.style.opacity = to;
        }
    }
}
