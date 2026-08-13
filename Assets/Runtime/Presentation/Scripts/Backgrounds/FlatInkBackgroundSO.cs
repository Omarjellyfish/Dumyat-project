using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// One considered flat colour, no motion. The honest choice for a dense slide where anything
    /// behind the type is noise.
    /// </summary>
    [CreateAssetMenu(fileName = "BG_FlatInk", menuName = "Dumyat/Presentation/Background/Flat Ink")]
    public sealed class FlatInkBackgroundSO : SlideBackgroundSO
    {
        [SerializeField, Tooltip("Delta's panel ground is #081A22.")]
        private Color _color = new Color(0.031f, 0.102f, 0.133f, 1f);

        public override Color GroundColor => _color;
    }
}
