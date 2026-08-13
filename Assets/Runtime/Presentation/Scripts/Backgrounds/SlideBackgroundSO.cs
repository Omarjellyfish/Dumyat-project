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
        /// Generated image, or null for a flat colour. Returned textures are owned by the caller.
        /// </summary>
        public virtual Texture2D CreateImage() => null;

        /// <summary>
        /// Vertical ramp helper. Delegates to <see cref="GradientRamp"/>, which is shared with the
        /// scene-slide scrim — the sRGB and row-order reasoning lives there, once.
        /// </summary>
        protected static Texture2D CreateVerticalRamp(int height, System.Func<float, Color> sample) =>
            GradientRamp.CreateVertical(height, sample, "GeneratedBackdropRamp");
    }
}
