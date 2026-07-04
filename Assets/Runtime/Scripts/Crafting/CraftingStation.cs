using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public class CraftingStation : MonoBehaviour
    {
        private ISoundService _sound;

        [Inject]
        private void Inject(ISoundService sound) => _sound = sound;

        [Header("Validation References")]
        [SerializeField] private Rigidbody _rootBody;
        [SerializeField] private CraftingRecipe _recipe;
        [SerializeField] private GameObject _hologramObject;

        [Header("Blueprint Selection")]
        [SerializeField] private Transform _spawnPoint;
        [SerializeField] private Transform _spawnPointHologram;
        [SerializeField] private CraftingRecipe[] _availableRecipes;

        [Header("Spawn Audio")]
        [SerializeField] private AudioClip[] _spawnClips;
        [SerializeField, Range(0f, 1f)] private float _spawnSfxVolume = 0.8f;

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
                if (recipe.HologramPrefab != null)
                {
                    _hologramObject = Instantiate(recipe.HologramPrefab, _spawnPointHologram.position, _spawnPointHologram.rotation);
                }
                

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

                PlaySpawnAudio();

                EventBus.Publish(new RecipeSelectedEvent(recipe, this));
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

        private void PlaySpawnAudio()
        {
            if (_spawnPoint == null) return;

            _sound?.PlayRandomSoundFxAtPosition(
                _spawnClips, _spawnPoint, _spawnSfxVolume);
        }
    }
}
