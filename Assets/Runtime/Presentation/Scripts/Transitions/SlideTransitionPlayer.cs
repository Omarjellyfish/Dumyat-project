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

        /// <summary>
        /// One screen-sized target, reused for every StageDissolve. Allocated on first use rather than in
        /// Awake because a deck that never crosses between places should never pay for it.
        /// </summary>
        private RenderTexture _freezeTexture;

        /// <summary>
        /// Panel-space size captured at freeze time. Held as a field because the push and wipe both need
        /// it every frame, and re-reading resolvedStyle mid-animation would pick up a resize halfway.
        /// </summary>
        private Vector2 _panelSize = new Vector2(1920f, 1080f);

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

        /// <summary>
        /// Covers the screen. Yield on this, then swap the slide, then yield on PlayIn.
        ///
        /// <paramref name="incoming"/> is the slide being arrived at, needed by LocationSlate so it can
        /// name the destination while the screen is dark. Every other kind ignores it.
        /// </summary>
        public IEnumerator PlayOut(SlideTransitionSO transition, SlideSO incoming = null)
        {
            IsPlaying = true;

            if (_view == null || !_view.IsReady || transition == null ||
                transition.Kind == SlideTransitionKind.Cut)
                yield break;

            if (transition.UsesFreeze)
            {
                // Nothing is covered here — the still IS the cover, and it is identical to what was
                // already on screen, so the swap behind it is invisible without a single dark frame.
                GrabFreezeFrame();
                yield break;
            }

            if (transition.Kind == SlideTransitionKind.CrossDissolve)
            {
                // Fades the content only, leaving the backdrop in place — text to text.
                yield return Fade(_view.Content, 1f, 0f, transition.OutDuration, transition);
                yield break;
            }

            if (transition.Kind == SlideTransitionKind.LocationSlate) ShowSlate(incoming);

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

                if (transition.UsesFreeze)
                {
                    yield return ClearFreeze(transition);
                    yield break;
                }

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
                // left behind an opaque cover, with invisible content, with a slate on screen, or with a
                // stale still frozen over a live stage. Every one of those looks like the deck has hung.
                if (_view != null && _view.IsReady)
                {
                    _view.Cover.style.opacity = 0f;
                    _view.Content.style.opacity = 1f;
                    _view.Slate.AddToClassList("hidden");
                    HideFreeze();
                }
                IsPlaying = false;
            }
        }

        /// <summary>
        /// Writes the destination onto the slate. Text comes from the slide itself
        /// (<see cref="SlideSO.SlateTitle"/>), so no slide type is named here.
        /// </summary>
        private void ShowSlate(SlideSO incoming)
        {
            string title = incoming != null ? incoming.SlateTitle : null;
            if (string.IsNullOrEmpty(title))
            {
                // A slate with nothing to say is worse than no slate — it is a second of dead screen.
                _view.Slate.AddToClassList("hidden");
                return;
            }

            _view.Slate.RemoveFromClassList("hidden");
            _view.SlateTitle.text = title;

            string sub = incoming.SlateSubtitle;
            bool hasSub = !string.IsNullOrEmpty(sub);
            _view.SlateArabic.EnableInClassList("hidden", !hasSub);
            // Raw text: the ui-arabic rule on .slate-ar switches the advanced generator on, which shapes
            // and reorders natively. Pre-shaping it here would shape it twice.
            if (hasSub) _view.SlateArabic.text = sub;
        }

        /// <summary>
        /// Renders the live stage into <see cref="_freezeTexture"/> and shows it full-screen.
        ///
        /// THE CAMERA IS BORROWED AND GIVEN BACK IN THE SAME FRAME, WITH NO YIELD IN BETWEEN. That is the
        /// entire safety argument: StagePrewarmer once pointed the main camera at an off-screen texture
        /// and yielded, and any interruption left the deck rendering into a 256x144 buffer for the rest of
        /// the session with no visible cause. Set, Render, restore, synchronously — and in a finally, so
        /// even an exception inside Render cannot strand it.
        /// </summary>
        private void GrabFreezeFrame()
        {
            Camera cam = Camera.main;
            if (cam == null || _view == null || !_view.IsReady) return;

            EnsureFreezeTexture();
            if (_freezeTexture == null) return;

            RenderTexture previous = cam.targetTexture;
            try
            {
                cam.targetTexture = _freezeTexture;
                cam.Render();
            }
            finally
            {
                cam.targetTexture = previous;
            }

            // The still is sized in PIXELS, and the clipper is reset to full size. A wipe shrinks the
            // clipper, so the still inside it must hold its own dimensions or the image squashes instead
            // of the live scene being revealed past a straight edge.
            _panelSize = PanelSize();
            VisualElement clip = _view.FreezeClip;
            VisualElement freeze = _view.Freeze;

            clip.RemoveFromClassList("hidden");
            clip.style.left = 0f;
            clip.style.top = 0f;
            clip.style.width = _panelSize.x;
            clip.style.height = _panelSize.y;

            freeze.style.backgroundImage =
                new StyleBackground(Background.FromRenderTexture(_freezeTexture));
            freeze.style.width = _panelSize.x;
            freeze.style.height = _panelSize.y;
            freeze.style.opacity = 1f;
            freeze.style.scale = new StyleScale(new Scale(Vector3.one));
            freeze.style.translate = new StyleTranslate(new Translate(0f, 0f));
        }

        /// <summary>
        /// Panel-space size of the deck, which is what the still and the clipper are measured in.
        /// Falls back to the reference resolution when layout has not resolved yet — better a correct
        /// aspect than a zero-sized still that renders nothing.
        /// </summary>
        private Vector2 PanelSize()
        {
            float w = _view.Ground.resolvedStyle.width;
            float h = _view.Ground.resolvedStyle.height;
            if (w < 1f || h < 1f) return new Vector2(1920f, 1080f);
            return new Vector2(w, h);
        }

        /// <summary>Routes to whichever way this transition gets rid of the still.</summary>
        private IEnumerator ClearFreeze(SlideTransitionSO transition)
        {
            switch (transition.Kind)
            {
                case SlideTransitionKind.StagePush:        return PushFreeze(transition);
                case SlideTransitionKind.StageZoomThrough: return ZoomFreeze(transition);
                case SlideTransitionKind.StageWipe:        return WipeFreeze(transition);
                default:                                   return DissolveFreeze(transition);
            }
        }

        /// <summary>
        /// Dissolves the still away, drifting it forward a couple of percent as it goes.
        ///
        /// The drift is what stops this reading as a plain fade: a still that only loses opacity looks
        /// like a dropped frame, while one that eases very slightly toward the viewer reads as the camera
        /// having moved on. Two percent is deliberately almost invisible — more than that and the seam
        /// between the still and the live render behind it becomes obvious at the edges.
        /// </summary>
        private IEnumerator DissolveFreeze(SlideTransitionSO transition)
        {
            VisualElement freeze = _view.Freeze;
            const float endScale = 1.02f;

            yield return Drive(transition, k =>
            {
                freeze.style.opacity = 1f - k;
                float s = Mathf.LerpUnclamped(1f, endScale, k);
                freeze.style.scale = new StyleScale(new Scale(new Vector3(s, s, 1f)));
            });

            HideFreeze();
        }

        /// <summary>
        /// Slides the still bodily off screen. No fade at all: the still stays fully opaque the whole
        /// way, because a push that also fades reads as an accident rather than a move.
        /// </summary>
        private IEnumerator PushFreeze(SlideTransitionSO transition)
        {
            VisualElement freeze = _view.Freeze;
            Vector2 exit = ExitOffset(transition.Direction, _panelSize);

            yield return Drive(transition, k =>
                freeze.style.translate = new StyleTranslate(new Translate(exit.x * k, exit.y * k)));

            HideFreeze();
        }

        /// <summary>
        /// Punches the still toward the viewer and fades it late, so the scale is felt before the image
        /// goes. Fading on a curve of k squared keeps the still readable for the first half of the move.
        /// </summary>
        private IEnumerator ZoomFreeze(SlideTransitionSO transition)
        {
            VisualElement freeze = _view.Freeze;
            float target = Mathf.Max(1.02f, transition.ZoomScale);

            yield return Drive(transition, k =>
            {
                float s = Mathf.LerpUnclamped(1f, target, k);
                freeze.style.scale = new StyleScale(new Scale(new Vector3(s, s, 1f)));
                freeze.style.opacity = 1f - k * k;
            });

            HideFreeze();
        }

        /// <summary>
        /// Shrinks the clipper so a straight edge travels across the frame, revealing the live stage.
        /// The still is translated to compensate when the clipper's own origin moves, which is what keeps
        /// the image pinned to the screen instead of sliding along with the edge.
        /// </summary>
        private IEnumerator WipeFreeze(SlideTransitionSO transition)
        {
            VisualElement clip = _view.FreezeClip;
            VisualElement freeze = _view.Freeze;
            float w = _panelSize.x, h = _panelSize.y;

            yield return Drive(transition, k =>
            {
                switch (transition.Direction)
                {
                    case TransitionDirection.Left:   // edge travels leftward, revealing from the left
                        clip.style.left = w * k;
                        clip.style.width = w * (1f - k);
                        freeze.style.translate = new StyleTranslate(new Translate(-w * k, 0f));
                        break;
                    case TransitionDirection.Up:
                        clip.style.top = h * k;
                        clip.style.height = h * (1f - k);
                        freeze.style.translate = new StyleTranslate(new Translate(0f, -h * k));
                        break;
                    case TransitionDirection.Down:
                        clip.style.height = h * (1f - k);
                        break;
                    default:                          // Right
                        clip.style.width = w * (1f - k);
                        break;
                }
            });

            HideFreeze();
        }

        /// <summary>Where the still ends up when pushed clean off the frame.</summary>
        private static Vector2 ExitOffset(TransitionDirection direction, Vector2 panel)
        {
            switch (direction)
            {
                case TransitionDirection.Right: return new Vector2(panel.x, 0f);
                case TransitionDirection.Up:    return new Vector2(0f, -panel.y);
                case TransitionDirection.Down:  return new Vector2(0f, panel.y);
                default:                        return new Vector2(-panel.x, 0f);
            }
        }

        /// <summary>
        /// The one clock every freeze transition runs on: steps 0 → 1 through the asset's own easing
        /// curve on unscaled time. Having a single driver is why four different mechanisms cannot end up
        /// with four subtly different notions of how long "0.55 seconds" is.
        /// </summary>
        private IEnumerator Drive(SlideTransitionSO transition, System.Action<float> step)
        {
            float duration = Mathf.Max(0.01f, transition.InDuration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                step(transition.Evaluate(Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            step(1f);
        }

        private void HideFreeze()
        {
            if (_view == null || !_view.IsReady) return;

            VisualElement clip = _view.FreezeClip;
            VisualElement freeze = _view.Freeze;

            freeze.style.opacity = 0f;
            freeze.style.scale = new StyleScale(new Scale(Vector3.one));
            freeze.style.translate = new StyleTranslate(new Translate(0f, 0f));

            // The clipper is put back to full size as well. Leaving it wiped down to zero width would
            // make the NEXT transition's still invisible, which is a bug that only shows up one slide
            // later and looks nothing like its cause.
            clip.style.left = 0f;
            clip.style.top = 0f;
            clip.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
            clip.style.height = new StyleLength(new Length(100f, LengthUnit.Percent));
            clip.AddToClassList("hidden");

            // The texture is kept, not released: one screen-sized target reused for every place change
            // beats allocating and freeing one per transition in front of an audience.
        }

        private void EnsureFreezeTexture()
        {
            int w = Mathf.Max(256, Screen.width);
            int h = Mathf.Max(144, Screen.height);

            if (_freezeTexture != null && _freezeTexture.width == w && _freezeTexture.height == h) return;

            ReleaseFreezeTexture();
            _freezeTexture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                name = "PresentationFreezeFrame",
            };
            _freezeTexture.Create();
        }

        private void ReleaseFreezeTexture()
        {
            if (_freezeTexture == null) return;
            _freezeTexture.Release();
            Destroy(_freezeTexture);
            _freezeTexture = null;
        }

        private void OnDestroy() => ReleaseFreezeTexture();

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
