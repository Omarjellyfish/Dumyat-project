using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Attach to a physical color sample in the scene (e.g. a dot on a paint palette).
    /// Touching it with the spray bottle's (or pen's) tip trigger picks up this color.
    /// </summary>
    public class ColorSwatch : MonoBehaviour
    {
        [SerializeField] private Color _swatchColor = Color.white;
        public Color SwatchColor => _swatchColor;

        private void OnValidate()
        {
            // Keeps the swatch's own visible color in sync with what it actually hands out,
            // so what the player sees is what they'll get when they touch it.
            Renderer rend = GetComponent<Renderer>();
            if (rend != null && rend.sharedMaterial != null)
            {
                rend.sharedMaterial.color = _swatchColor;
            }
        }
    }
}