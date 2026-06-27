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

        [Tooltip("If true, rotation is not checked for this slot (useful for cylindrical/symmetric pieces)")]
        public bool IgnoreRotation;

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

        [Header("Root Piece")]
        [Tooltip("The prefab for the root structural piece (e.g. Chair Seat)")]
        [SerializeField] private GameObject _rootPrefab;

        [SerializeField] private PartBlueprint[] _requiredParts;

        public string RecipeName => _recipeName;
        public GameObject RootPrefab => _rootPrefab;
        public PartBlueprint[] RequiredParts => _requiredParts;

#if UNITY_EDITOR
        public void Editor_SetData(string recipeName, GameObject rootPrefab, PartBlueprint[] parts)
        {
            _recipeName = recipeName;
            _rootPrefab = rootPrefab;
            _requiredParts = parts;
        }
#endif
    }
}
