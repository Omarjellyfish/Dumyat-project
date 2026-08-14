using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// What sits behind a slide's type.
    ///
    /// WHY THIS PRODUCES A TEXTURE RATHER THAN STYLING AN ELEMENT DIRECTLY:
    /// UI Toolkit cannot draw gradients, box-shadows or blur (a documented constraint of this
    /// project). Generating a small Texture2D and letting the panel stretch it gives real gradients
    /// and vignettes with no shader, no extra camera and no scene setup — and it keeps the type layer
    /// as plain UI Toolkit on top.
    ///
    /// The caller owns the returned texture. <see cref="BackdropController"/> caches one per asset and
    /// destroys them on teardown, so generation happens once per background, not once per slide.
    /// </summary>
    public abstract class SlideBackgroundSO : ScriptableObject
    {
        /// <summary>Flat colour painted under the image. Used alone when <see cref="CreateImage"/> is null.</summary>
        public abstract Color GroundColor { get; }

        /// <summary>
        /// Generated image, or null for a flat colour. Returned textures are owned by the caller
        /// UNLESS <see cref="OwnsCreatedImage"/> says otherwise.
        /// </summary>
        public virtual Texture2D CreateImage() => null;

        /// <summary>
        /// Whether the caller may destroy what <see cref="CreateImage"/> returned.
        ///
        /// True for every background that GENERATES its texture, which was all of them until
        /// <see cref="ImageBackgroundSO"/> started handing back an authored asset instead. The
        /// controller destroys its cache on teardown, and destroying an asset texture deletes it from
        /// the project — so a background that returns something it did not create must say so here.
        /// </summary>
        public virtual bool OwnsCreatedImage => true;

        /// <summary>
        /// An optional second layer painted OVER the image, for legibility. Always generated, so the
        /// caller always owns it.
        ///
        /// It has to be a separate texture on a separate element: UI Toolkit cannot stack two
        /// background images on one element, and baking the veil into a copy of the artwork would mean
        /// duplicating a full-resolution texture just to darken it.
        /// </summary>
        public virtual Texture2D CreateVeil() => null;

        /// <summary>
        /// Vertical ramp helper. Delegates to <see cref="GradientRamp"/>, which is shared with the
        /// scene-slide scrim — the sRGB and row-order reasoning lives there, once.
        /// </summary>
        protected static Texture2D CreateVerticalRamp(int height, System.Func<float, Color> sample) =>
            GradientRamp.CreateVertical(height, sample, "GeneratedBackdropRamp");
    }
}
