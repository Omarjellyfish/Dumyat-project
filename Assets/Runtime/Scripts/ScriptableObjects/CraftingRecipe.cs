using UnityEngine;

namespace DumyatRuntime
{
    [System.Serializable]
    public struct PartBlueprint
    {
        [Tooltip("Human-readable label, e.g. 'Front Leg'")]
        public string SlotName;

        [Header("Spatial Requirements")]
        public Vector3 ExpectedLocalPosition;
        public Quaternion ExpectedLocalRotation;

        [Tooltip("Maximum allowed distance from expected position")]
        public float PositionTolerance;

        [Tooltip("Maximum allowed angle deviation in degrees")]
        public float RotationTolerance;

        [Header("Proportional Size Requirements")]
        [Tooltip("Expected bounding box dimensions in root local space")]
        public Vector3 ExpectedSize;

        [Tooltip("Per-axis margin of error for size")]
        public Vector3 SizeTolerance;
    }

    [CreateAssetMenu(fileName = "NewCraftingRecipe", menuName = "Dumyat/Crafting/Recipe")]
    public class CraftingRecipe : ScriptableObject
    {
        [SerializeField] private string _recipeName;
        [SerializeField] private PartBlueprint[] _requiredParts;

        public string RecipeName => _recipeName;
        public PartBlueprint[] RequiredParts => _requiredParts;

#if UNITY_EDITOR
        public void Editor_SetData(string recipeName, PartBlueprint[] parts)
        {
            _recipeName = recipeName;
            _requiredParts = parts;
        }
#endif
    }
}
