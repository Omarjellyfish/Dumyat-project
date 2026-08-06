using TMPro;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Makes TextMeshPro text read as cut into, or burned onto, a physical surface.
    ///
    /// Both effects come down to the same two shader features — an offset soft edge (TMP's
    /// "underlay") and a coloured rim (its "outline") — set up the same fiddly way each time:
    /// check the shader actually has the properties, enable the gating keyword, write the values,
    /// then repad the mesh so the result is not clipped at the glyph's own edge. Miss any one of
    /// those and the call silently does nothing.
    ///
    /// WHY NOT GLOW. TextMeshPro's full Distance Field shader has a real Glow feature and it is the
    /// obvious choice for a hot ember. The fonts in this project render with
    /// <c>TextMeshPro/Mobile/Distance Field</c>, which has NO glow properties at all — a glow-based
    /// version compiles, runs, and produces nothing. Underlay and outline exist on both shaders,
    /// so everything here is built on those.
    ///
    /// Static because it is a pure function of a material — no state, nothing to inject.
    /// </summary>
    public static class TmpEngraving
    {
        public static bool HasUnderlay(Material material) =>
            material != null && material.HasProperty(ShaderUtilities.ID_UnderlayColor);

        public static bool HasOutline(Material material) =>
            material != null && material.HasProperty(ShaderUtilities.ID_OutlineWidth);

        /// <summary>
        /// The offset soft edge. A DARK colour offset down-right reads as a shadow the text casts on
        /// the surface; a LIGHT one reads as the lit lower lip of a cut. Same feature, opposite
        /// stories — which is why the colour is the caller's decision, not this method's.
        /// </summary>
        /// <returns>False when the shader has no underlay, so the caller can skip its own work.</returns>
        public static bool TrySetUnderlay(Material material, Color colour, Vector2 offset,
                                          float dilate, float softness)
        {
            if (!HasUnderlay(material)) return false;

            material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, colour);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, offset.x);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, offset.y);
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate, dilate);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, softness);
            return true;
        }

        /// <summary>The coloured rim around the letterform — a burn edge, or the bright cut of a chisel.</summary>
        /// <returns>False when the shader has no outline.</returns>
        public static bool TrySetOutline(Material material, Color colour, float width)
        {
            if (!HasOutline(material)) return false;

            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            material.SetColor(ShaderUtilities.ID_OutlineColor, colour);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
            return true;
        }

        /// <summary>
        /// Call once after changing any of the above. Both features draw INSIDE the character quad,
        /// so without the extra padding the edge is cut off at the letter and never leaves the
        /// stroke — the single most common reason this all looks like it did nothing.
        /// </summary>
        public static void Repad(TMP_Text label)
        {
            if (label != null) label.UpdateMeshPadding();
        }
    }
}
