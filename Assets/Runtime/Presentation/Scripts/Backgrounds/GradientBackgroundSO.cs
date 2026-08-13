using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// A vertical two- or three-stop gradient with an optional darkened base — the "Deep Water"
    /// treatment that is the deck's default, and the same asset type used for the warm "Workshop
    /// Grain" variant. Different mood, second asset, no second script.
    /// </summary>
    [CreateAssetMenu(fileName = "BG_Gradient", menuName = "Dumyat/Presentation/Background/Gradient")]
    public sealed class GradientBackgroundSO : SlideBackgroundSO
    {
        [SerializeField, Tooltip("Colour at the top of the frame.")]
        private Color _top = new Color(0.055f, 0.165f, 0.204f, 1f);

        [SerializeField, Tooltip("Colour at the bottom of the frame.")]
        private Color _bottom = new Color(0.020f, 0.059f, 0.082f, 1f);

        [SerializeField, Range(0f, 1f),
         Tooltip("Where the midpoint sits. 0.5 is a straight blend; lower pushes the darker half up.")]
        private float _midpoint = 0.5f;

        [SerializeField, Range(0f, 1f),
         Tooltip("Extra darkening at the very bottom edge, so bottom-left type always has contrast " +
                 "under it. 0 disables it.")]
        private float _baseShade = 0.25f;

        [SerializeField, Range(64, 512),
         Tooltip("Ramp height in pixels. 256 is plenty — the panel stretches it and banding is not " +
                 "visible below that on a 1080p projector.")]
        private int _resolution = 256;

        public override Color GroundColor => _bottom;

        public override Texture2D CreateImage()
        {
            return CreateVerticalRamp(_resolution, t =>
            {
                // Bias t around the midpoint so the stop can be moved without adding a third colour.
                float k = _midpoint <= 0f ? 1f
                        : _midpoint >= 1f ? 0f
                        : t < _midpoint
                            ? Mathf.InverseLerp(0f, _midpoint, t) * 0.5f
                            : 0.5f + Mathf.InverseLerp(_midpoint, 1f, t) * 0.5f;

                Color c = Color.Lerp(_top, _bottom, k);

                if (_baseShade > 0f)
                {
                    // Only the bottom fifth is touched, eased so there is no visible seam.
                    float shade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.80f, 1f, t)) * _baseShade;
                    c = Color.Lerp(c, Color.black, shade);
                }

                c.a = 1f;
                return c;
            });
        }
    }
}
