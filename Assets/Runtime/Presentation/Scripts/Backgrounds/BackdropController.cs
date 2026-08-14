using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Paints the backdrop layer from a <see cref="SlideBackgroundSO"/>, and owns the lifetime of the
    /// textures those assets produce.
    ///
    /// The cache is the reason this is a component rather than a static helper: generated textures
    /// are unmanaged and must be destroyed. One per background asset is created on first use and
    /// released together in OnDestroy, so switching between the same handful of backgrounds across
    /// 55 slides allocates a handful of textures, not 55.
    ///
    /// NOT EVERYTHING IN THE CACHE MAY BE DESTROYED. <see cref="ImageBackgroundSO"/> hands back an
    /// authored project asset rather than something it generated, and calling Destroy on that deletes
    /// the texture from the project. Each entry therefore records whether its image is ours to free;
    /// the veil always is, because it is always generated.
    /// </summary>
    public sealed class BackdropController : MonoBehaviour
    {
        [SerializeField] private PresentationView _view;

        /// <summary>What one background asset resolved to, and what of it we are allowed to free.</summary>
        private readonly struct Painted
        {
            public readonly Texture2D Image;
            public readonly bool OwnsImage;
            public readonly Texture2D Veil;

            public Painted(Texture2D image, bool ownsImage, Texture2D veil)
            {
                Image = image;
                OwnsImage = ownsImage;
                Veil = veil;
            }
        }

        private readonly Dictionary<SlideBackgroundSO, Painted> _cache =
            new Dictionary<SlideBackgroundSO, Painted>();

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
            HideVeil();
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
                surface.style.unityBackgroundScaleMode = new StyleEnum<ScaleMode>(StyleKeyword.Null);
                HideVeil();
                return;
            }

            surface.style.backgroundColor = background.GroundColor;

            Painted painted = Resolve(background);

            // A flat-colour background legitimately has no image; clearing rather than assigning null
            // keeps the previous slide's ramp from lingering underneath.
            surface.style.backgroundImage = painted.Image != null
                ? new StyleBackground(painted.Image)
                : new StyleBackground(StyleKeyword.None);

            // Generated ramps are 1 px wide and must stretch. An authored image says how it wants to be
            // fitted, because cropping a poster and letterboxing it are both right in different decks.
            surface.style.unityBackgroundScaleMode =
                background is ImageBackgroundSO image ? image.Fit : ScaleMode.StretchToFill;

            ApplyVeil(painted.Veil);
        }

        /// <summary>
        /// The veil rides on its OWN element rather than the scene slides' `#scrim`.
        ///
        /// The scrim is toggled by three different renderers, and the director paints the backdrop
        /// BEFORE it calls them — so the outgoing renderer's Hide() would switch the veil straight back
        /// off, on exactly the slide that needed it. A layer nothing else touches has no such ordering
        /// to get right.
        /// </summary>
        private void ApplyVeil(Texture2D veil)
        {
            if (_view.BackdropVeil == null) return;

            if (veil == null) { HideVeil(); return; }

            _view.BackdropVeil.RemoveFromClassList("hidden");
            _view.BackdropVeil.style.backgroundImage = new StyleBackground(veil);
            _view.BackdropVeil.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
        }

        private void HideVeil()
        {
            if (_view.BackdropVeil == null) return;
            _view.BackdropVeil.AddToClassList("hidden");
            _view.BackdropVeil.style.backgroundImage = new StyleBackground(StyleKeyword.None);
        }

        private Painted Resolve(SlideBackgroundSO background)
        {
            if (_cache.TryGetValue(background, out Painted cached)) return cached;

            var created = new Painted(background.CreateImage(), background.OwnsCreatedImage,
                                      background.CreateVeil());
            _cache[background] = created;      // cache nulls too, so a flat colour is not re-probed
            return created;
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<SlideBackgroundSO, Painted> entry in _cache)
            {
                // Only what we generated. An ImageBackgroundSO's texture is a project asset, and
                // destroying it deletes it from disk in the Editor.
                if (entry.Value.OwnsImage && entry.Value.Image != null) Destroy(entry.Value.Image);
                if (entry.Value.Veil != null) Destroy(entry.Value.Veil);
            }
            _cache.Clear();
        }
    }
}
