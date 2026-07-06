using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Marker component. Drag onto any grabbable object that is purely cosmetic.
    /// The CraftingValidator excludes these from structural checks and bounding box calculations.
    /// </summary>
    public class DecorationPiece : MonoBehaviour, IDecoration { }
}
