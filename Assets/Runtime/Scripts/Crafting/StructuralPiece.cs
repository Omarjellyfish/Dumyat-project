using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Marker component. Drag onto any grabbable object that is load-bearing.
    /// The CraftingValidator uses this to identify structural parts.
    /// </summary>
    public class StructuralPiece : MonoBehaviour, IStructuralPiece { }
}
