using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Paints the scene-slide scrim once and owns its texture.
    ///
    /// The ramp shape is the design decision worth keeping: dark at the very top, dark at the bottom,
    /// and fully clear through the middle third. That is what lets the eyebrow sit legibly over sky and
    /// the lockup over water without dimming the subject of the shot — a flat overlay would wash the
    /// whole scene out, and Delta's frosted panel would hide it outright.
    /// </summary>
    public sealed class ScrimPainter : MonoBehaviour
    {
        [SerializeField] private PresentationView _view;

        [SerializeField, Tooltip("Colour the scrim darkens towards. Delta's deepest water, near-black.")]
        private Color _shade = new Color(0.016f, 0.055f, 0.075f, 1f);

        [SerializeField, Range(0f, 1f), Tooltip("Opacity at the very top edge, behind the eyebrow.")]
        private float _topStrength = 0.72f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity at the very bottom edge, behind the lockup.")]
        private float _bottomStrength = 0.86f;

        [SerializeField, Range(0f, 0.5f), Tooltip("How far down the top band fades out.")]
        private float _topFalloff = 0.30f;

        [SerializeField, Range(0f, 0.6f), Tooltip("How far up the bottom band fades out.")]
        private float _bottomFalloff = 0.42f;

        private Texture2D _texture;

        private void Awake()
        {
            if (_view == null) _view = GetComponentInParent<PresentationView>();
            if (_view == null || !_view.IsReady)
            {
                Debug.LogWarning("[ScrimPainter] no PresentationView — scene slides will have no scrim.", this);
                return;
            }

            _texture = GradientRamp.CreateVertical(256, Sample, "GeneratedScrimRamp");
            _view.Scrim.style.backgroundImage = new StyleBackground(_texture);
            _view.Scrim.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
        }

        private Color Sample(float t)
        {
            // t = 0 top, t = 1 bottom. SmoothStep on both ends so neither band shows an edge.
            float top = _topFalloff <= 0f ? 0f
                : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_topFalloff, 0f, t)) * _topStrength;

            float bottom = _bottomFalloff <= 0f ? 0f
                : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - _bottomFalloff, 1f, t)) * _bottomStrength;

            Color c = _shade;
            c.a = Mathf.Clamp01(Mathf.Max(top, bottom));
            return c;
        }

        private void OnDestroy()
        {
            if (_texture != null) Destroy(_texture);
        }
    }
}
