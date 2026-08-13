using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Builds one-pixel-wide vertical ramp textures for the deck to stretch across an element.
    ///
    /// WHY THIS EXISTS AS A SHARED HELPER:
    /// UI Toolkit cannot draw gradients, so anything in this deck that needs one has to bake a texture.
    /// Two things need it — the slide backdrop and the scene-slide scrim — and the awkward details are
    /// the same for both: the ramp must be flagged sRGB or a linear-colour-space project renders it
    /// washed out, row 0 is the BOTTOM of a Unity texture so t must be inverted, and the texture is
    /// unmanaged so somebody has to destroy it.
    ///
    /// Getting the sRGB flag wrong is not a subtle bug — it made every backdrop visibly pale in Phase 1
    /// — which is exactly why the reasoning lives here once instead of in each caller.
    /// </summary>
    public static class GradientRamp
    {
        /// <summary>
        /// Creates a 1 x <paramref name="height"/> ramp. <paramref name="sample"/> receives t = 0 at the
        /// TOP of the frame and t = 1 at the bottom. The caller owns the returned texture.
        /// </summary>
        public static Texture2D CreateVertical(int height, System.Func<float, Color> sample,
                                               string name = "GeneratedRamp")
        {
            height = Mathf.Max(2, height);

            // linear:false marks the data as sRGB. The colours handed in are hex values from the Delta
            // palette, and this project renders in Linear colour space.
            var tex = new Texture2D(1, height, TextureFormat.RGBA32, false, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };

            for (int y = 0; y < height; y++)
            {
                float t = 1f - (y / (float)(height - 1));   // row 0 is the bottom row
                tex.SetPixel(0, y, sample(t));
            }

            tex.Apply(false, false);
            return tex;
        }
    }
}
