using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Authored artwork behind a slide's type — the poster treatment on slide 01, and anything else
    /// that wants a real image rather than a generated ramp.
    ///
    /// WHY THIS IS A BACKGROUND AND NOT A FIELD ON THE SLIDE:
    /// a background is already defined as "what sits behind a slide's type", and every slide already
    /// resolves one. Putting the poster art here means the opening slide keeps its oversized title, its
    /// kicker, its hairline and its one-off reveal with NOTHING in <see cref="TextSlideSO"/> or the text
    /// renderer changed — the artwork is simply what the backdrop is painted with. Any other slide can
    /// have one by assigning a second asset.
    ///
    /// THE VEIL IS NOT OPTIONAL POLISH:
    /// pale type over an unmodified photograph is unreadable at the back of a room, and this deck's type
    /// is deliberately light-weight. The veil is a three-stop vertical ramp so the darkening can sit
    /// where the type is — heavy at the top and bottom, clear through the middle — instead of flattening
    /// the whole image to make one line legible.
    /// </summary>
    [CreateAssetMenu(fileName = "BG_Image", menuName = "Dumyat/Presentation/Background/Image")]
    public sealed class ImageBackgroundSO : SlideBackgroundSO
    {
        [Header("Artwork")]
        [SerializeField, Tooltip("The image. Author it at the projector's aspect (16:9) and it fills " +
                                 "exactly; anything else is cropped or letterboxed per Fit below.")]
        private Texture2D _image;

        [SerializeField, Tooltip("ScaleAndCrop fills the frame and crops the overhang — right for a " +
                                 "poster.\nScaleToFit shows the whole image and pads with Ground — " +
                                 "right for artwork you framed and must not lose the edges of.")]
        private ScaleMode _fit = ScaleMode.ScaleAndCrop;

        [SerializeField, Tooltip("Painted under the image: the letterbox bars under ScaleToFit, and " +
                                 "the whole frame while no image is assigned.")]
        private Color _ground = new Color(0.016f, 0.078f, 0.106f, 1f);

        [Header("Legibility veil — over the image, under the type")]
        [SerializeField, Tooltip("Colour the veil darkens toward. The Delta ground by default, so the " +
                                 "image reads as sinking into the deck rather than being greyed out.")]
        private Color _veilColor = new Color(0.016f, 0.078f, 0.106f, 1f);

        [SerializeField, Range(0f, 1f), Tooltip("Opacity at the very top of the frame.")]
        private float _veilTop = 0.86f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity through the middle. Keep this low — it is the " +
                                                "band where the artwork actually shows.")]
        private float _veilMiddle = 0.42f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity at the very bottom, under the footer and the " +
                                                "progress rail.")]
        private float _veilBottom = 0.88f;

        [SerializeField, Range(0f, 1f), Tooltip("Where the middle stop sits, 0 = top, 1 = bottom. " +
                                                "Move it to follow where the title actually sits.")]
        private float _veilMidpoint = 0.5f;

        [SerializeField, Range(64, 512), Tooltip("Ramp height in pixels. 256 is past the point of " +
                                                 "visible banding on a 1080p projector.")]
        private int _veilResolution = 256;

        public Texture2D Image => _image;
        public ScaleMode Fit => _fit;

        public override Color GroundColor => _ground;

        /// <summary>The authored asset itself — no copy, no generation.</summary>
        public override Texture2D CreateImage() => _image;

        /// <summary>Never. This hands back a project asset; destroying it would delete it.</summary>
        public override bool OwnsCreatedImage => false;

        public override Texture2D CreateVeil()
        {
            // A fully transparent veil is a texture and a draw for nothing, so it is simply absent.
            if (_veilTop <= 0f && _veilMiddle <= 0f && _veilBottom <= 0f) return null;

            return CreateVerticalRamp(_veilResolution, t =>
            {
                float a = t < _veilMidpoint
                    ? Mathf.Lerp(_veilTop, _veilMiddle, SafeInverseLerp(0f, _veilMidpoint, t))
                    : Mathf.Lerp(_veilMiddle, _veilBottom, SafeInverseLerp(_veilMidpoint, 1f, t));

                // Smoothed so the two straight segments do not meet in a visible crease across the
                // middle of the image, which a linear join is clearly visible as on a large screen.
                Color c = _veilColor;
                c.a = Mathf.SmoothStep(0f, 1f, a);
                return c;
            });
        }

        /// <summary>InverseLerp that returns 0 rather than dividing by zero when the stop is at an edge.</summary>
        private static float SafeInverseLerp(float a, float b, float value) =>
            Mathf.Approximately(a, b) ? 0f : Mathf.InverseLerp(a, b, value);
    }
}
