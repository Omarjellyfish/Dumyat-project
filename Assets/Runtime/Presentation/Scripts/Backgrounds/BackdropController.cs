using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Paints the backdrop layer from a <see cref="SlideBackgroundSO"/>, and owns the lifetime of the
    /// textures those assets generate.
    ///
    /// The cache is the reason this is a component rather than a static helper: generated textures
    /// are unmanaged and must be destroyed. One per background asset is created on first use and
    /// released together in OnDestroy, so switching between the same handful of backgrounds across
    /// 58 slides allocates a handful of textures, not 58.
    /// </summary>
    public sealed class BackdropController : MonoBehaviour
    {
        [SerializeField] private PresentationView _view;

        private readonly Dictionary<SlideBackgroundSO, Texture2D> _cache =
            new Dictionary<SlideBackgroundSO, Texture2D>();

        private SlideBackgroundSO _current;

        private void Awake()
        {
            if (_view == null) _view = GetComponentInParent<PresentationView>();
            if (_view == null)
                Debug.LogWarning("[BackdropController] No PresentationView — backdrop will not update.", this);
        }

        /// <summary>
        /// Takes the backdrop out of the way entirely so a stage render shows through.
        ///
        /// Both the backdrop element AND the ground's own background have to go: `.presentation-root`
        /// carries an opaque Delta ground in USS, and hiding only the backdrop leaves that flat colour
        /// covering the 3D. Missing the second half of this is a convincing "the stage did not load" bug.
        ///
        /// It must be <see cref="PresentationView.Ground"/>, not <c>Root</c>. Root is the UIDocument
        /// container and is transparent already, so clearing it is a no-op that reads as correct — the
        /// deck shipped that way and every single scene slide showed chrome over a blank rectangle while
        /// the camera, the stage and the 3D were all working perfectly.
        /// </summary>
        public void Hide()
        {
            if (_view == null || !_view.IsReady) return;

            _view.Backdrop.AddToClassList("hidden");
            _view.Ground.style.backgroundColor = Color.clear;
            _current = null;                     // force a repaint when a text slide comes back
        }

        public void Apply(SlideBackgroundSO background)
        {
            if (_view == null || !_view.IsReady) return;

            _view.Backdrop.RemoveFromClassList("hidden");
            _view.Ground.style.backgroundColor = new StyleColor(StyleKeyword.Null);   // back to the USS ground

            // Repainting an unchanged backdrop would rebuild nothing but still churn style state.
            if (ReferenceEquals(background, _current)) return;
            _current = background;

            VisualElement surface = _view.Backdrop;

            if (background == null)
            {
                surface.style.backgroundColor = new StyleColor(StyleKeyword.Null);
                surface.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
                return;
            }

            surface.style.backgroundColor = background.GroundColor;

            Texture2D image = Resolve(background);
            // A flat-colour background legitimately has no image; clearing rather than assigning null
            // keeps the previous slide's ramp from lingering underneath.
            surface.style.backgroundImage = image != null
                ? new StyleBackground(image)
                : new StyleBackground(StyleKeyword.None);
        }

        private Texture2D Resolve(SlideBackgroundSO background)
        {
            if (_cache.TryGetValue(background, out Texture2D cached)) return cached;

            Texture2D created = background.CreateImage();
            _cache[background] = created;      // cache nulls too, so a flat colour is not re-probed
            return created;
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<SlideBackgroundSO, Texture2D> entry in _cache)
            {
                if (entry.Value != null) Destroy(entry.Value);
            }
            _cache.Clear();
        }
    }
}
