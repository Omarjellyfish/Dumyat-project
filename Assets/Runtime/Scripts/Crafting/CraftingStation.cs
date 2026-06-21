using UnityEngine;

namespace DumyatRuntime
{
    public class CraftingStation : MonoBehaviour
    {
        [Header("Validation References")]
        [SerializeField] private Rigidbody _rootBody;
        [SerializeField] private CraftingRecipe _recipe;

        [Header("Blueprint Selection")]
        [SerializeField] private Transform _spawnPoint;
        [SerializeField] private CraftingRecipe[] _availableRecipes;

        private GameObject _currentRootObject;

        public CraftingRecipe[] AvailableRecipes => _availableRecipes;

        public void SelectRecipe(CraftingRecipe recipe)
        {
            if (recipe == null) return;

            _recipe = recipe;

            if (_currentRootObject != null)
            {
                Destroy(_currentRootObject);
            }

            if (_spawnPoint != null && recipe.RootPrefab != null)
            {
                _currentRootObject = Instantiate(recipe.RootPrefab, _spawnPoint.position, _spawnPoint.rotation);

                if (_currentRootObject.TryGetComponent<Rigidbody>(out var rb))
                {
                    _rootBody = rb;

                    _rootBody.isKinematic = false;
                    _rootBody.useGravity = true;

                    FixedJoint[] joints = _currentRootObject.GetComponents<FixedJoint>();
                    foreach (var joint in joints)
                    {
                        if (joint.connectedBody == null)
                        {
                            Destroy(joint);
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"[CraftingStation] Spawned root {recipe.RootPrefab.name} is missing a Rigidbody.");
                }
            }
            else
            {
                Debug.LogWarning("[CraftingStation] Spawn point or Recipe Root Prefab is missing.");
            }
        }

        public void CheckBuild()
        {
            CraftingValidator.ValidateBuild(_rootBody, _recipe);
        }
    }
}
