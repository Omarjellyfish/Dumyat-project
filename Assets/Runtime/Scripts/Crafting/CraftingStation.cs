using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Place this on the designated crafting area in the scene.
    /// Holds a reference to the root piece and the recipe to validate against.
    /// Call CheckBuild() from a button press or interaction event.
    /// </summary>
    public class CraftingStation : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Rigidbody _rootBody;
        [SerializeField] private CraftingRecipe _recipe;

        /// <summary>
        /// Triggers the 3-tier validation against the assigned root and recipe.
        /// Hook this up to a button press or interaction event.
        /// </summary>
        public void CheckBuild()
        {
            if (_rootBody == null)
            {
                Debug.LogError("[CraftingStation] Root Rigidbody is not assigned.", this);
                return;
            }

            if (_recipe == null)
            {
                Debug.LogError("[CraftingStation] Crafting Recipe is not assigned.", this);
                return;
            }

            CraftingValidator.ValidateBuild(_rootBody, _recipe);
        }
    }
}
