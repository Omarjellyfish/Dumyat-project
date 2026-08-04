using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Stamps brush marks into a <see cref="WoodPaintSurface"/>'s paint RenderTexture.
    /// Marks are drawn in UV space: a single dot via <see cref="PaintAt"/>, or a
    /// continuous run of overlapping dots via <see cref="PaintStroke"/>.
    /// </summary>
    /// <remarks>
    /// The stamp material is built at runtime from <c>Hidden/Dumyat/PaintStamp</c> rather
    /// than taken from the Inspector. Marks are drawn with immediate-mode
    /// <see cref="Graphics.DrawTexture(Rect, Texture, Material)"/> outside of any SRP
    /// pass, and a URP shader driven that way has no valid per-frame constants - which is
    /// why stamps came out the wrong colour in a headset. A per-instance material also
    /// keeps the pen colour from being written back into a shared material asset.
    /// </remarks>
    public class PaintBrush : MonoBehaviour
    {
        private const string StampShaderName = "Hidden/Dumyat/PaintStamp";

        /// <summary>Overlap between consecutive dots, as a fraction of the brush size.</summary>
        private const float StrokeSpacingFactor = 0.35f;

        /// <summary>Backstop so one absurd UV jump cannot stall a frame.</summary>
        private const int MaxStampsPerStroke = 512;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        [SerializeField] private Texture2D _brushTexture;
        [Tooltip("Leave assigned. A serialized reference is what keeps the stamp shader in " +
                 "a player build - it is a Hidden shader with no material asset behind it, " +
                 "so nothing else would stop the build stripping it.")]
        [SerializeField] private Shader _stampShader;

        private Material _stampMaterial;
        private bool _shaderMissingReported;

        private void OnDestroy()
        {
            if (_stampMaterial != null)
                Destroy(_stampMaterial);
        }

        /// <summary>Stamps a single dot centred on <paramref name="uv"/>.</summary>
        /// <param name="size">Brush diameter in UV units (1 = the whole texture).</param>
        public void PaintAt(WoodPaintSurface wood, Vector2 uv, Color color, float size)
        {
            if (!TryBeginStroke(wood, color, out RenderTexture target))
                return;

            Stamp(target, uv, size);
            EndStroke();
        }

        /// <summary>
        /// Stamps a run of overlapping dots from <paramref name="fromUv"/> to
        /// <paramref name="toUv"/>. Use this instead of repeated <see cref="PaintAt"/>
        /// calls: at VR frame rates the tip moves far enough between frames that
        /// individual stamps read as a dotted trail rather than a stroke.
        /// </summary>
        public void PaintStroke(WoodPaintSurface wood, Vector2 fromUv, Vector2 toUv, Color color, float size)
        {
            if (!TryBeginStroke(wood, color, out RenderTexture target))
                return;

            float spacing = Mathf.Max(size * StrokeSpacingFactor, 0.0005f);
            int steps = Mathf.Clamp(
                Mathf.CeilToInt(Vector2.Distance(fromUv, toUv) / spacing), 1, MaxStampsPerStroke);

            for (int i = 0; i <= steps; i++)
                Stamp(target, Vector2.Lerp(fromUv, toUv, i / (float)steps), size);

            EndStroke();
        }

        private bool TryBeginStroke(WoodPaintSurface wood, Color color, out RenderTexture target)
        {
            target = null;

            if (wood == null || wood.PaintTexture == null)
                return false;

            if (_brushTexture == null)
            {
                Debug.LogWarning($"{nameof(PaintBrush)} on {name} has no brush texture assigned.", this);
                return false;
            }

            if (!TryGetStampMaterial())
                return false;

            _stampMaterial.SetColor(ColorId, color);
            _stampMaterial.SetTexture(MainTexId, _brushTexture);

            target = wood.PaintTexture;
            RenderTexture.active = target;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, target.width, target.height, 0);
            return true;
        }

        private void Stamp(RenderTexture target, Vector2 uv, float size)
        {
            float pixelX = uv.x * target.width;
            // The paint texture's origin is top-left in pixel-matrix space, UVs are bottom-left.
            float pixelY = (1f - uv.y) * target.height;
            float brushPixelSize = Mathf.Max(size * target.width, 1f);

            Rect rect = new Rect(
                pixelX - brushPixelSize * 0.5f,
                pixelY - brushPixelSize * 0.5f,
                brushPixelSize,
                brushPixelSize);

            Graphics.DrawTexture(rect, _brushTexture, _stampMaterial);
        }

        private void EndStroke()
        {
            GL.PopMatrix();
            RenderTexture.active = null;
        }

        private bool TryGetStampMaterial()
        {
            if (_stampMaterial != null)
                return true;

            Shader shader = _stampShader != null ? _stampShader : Shader.Find(StampShaderName);
            if (shader == null)
            {
                if (!_shaderMissingReported)
                {
                    _shaderMissingReported = true;
                    Debug.LogWarning(
                        $"{nameof(PaintBrush)} on {name} has no stamp shader. Painting is disabled. " +
                        $"Assign '{StampShaderName}' to its Stamp Shader field.", this);
                }
                return false;
            }

            _stampMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }
    }
}
