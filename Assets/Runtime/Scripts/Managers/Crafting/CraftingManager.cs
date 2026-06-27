using System;
using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    public class CraftingManager : MonoBehaviour, IManager
    {
        public static CraftingManager Singleton { get; private set; }

        public static Action<string> OnBlueprintUnlocked;

        [SerializeField] private RecipeDatabase _recipeDatabase;

        private CraftingSaveData _craftingData;

        private void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }
            Singleton = this;

            if (SaveManager.CurrentSaveData == null)
            {
                SaveManager.LoadGame();
            }

            _craftingData = SaveManager.CurrentSaveData.craftingData;
            foreach (var item in _craftingData.unlockedBlueprints) {
                Debug.Log(item);
            }
        }

        public bool IsBlueprintUnlocked(string recipeName)
        {
            if (_craftingData == null) return false;
            return _craftingData.unlockedBlueprints.Contains(recipeName);
        }

        public void UnlockBlueprint(string recipeName)
        {
            if (_craftingData == null) return;

            if (!_craftingData.unlockedBlueprints.Contains(recipeName))
            {
                _craftingData.unlockedBlueprints.Add(recipeName);
                SaveManager.SaveGameAsync();

                Debug.Log($"CraftingManager: Blueprint Unlocked -> '{recipeName}'");
                OnBlueprintUnlocked?.Invoke(recipeName);
            }
        }

        public List<CraftingRecipe> GetUnlockedRecipes()
        {
            List<CraftingRecipe> unlockedList = new List<CraftingRecipe>();
            if (_craftingData == null || _recipeDatabase == null) return unlockedList;

            foreach (var recipeName in _craftingData.unlockedBlueprints)
            {
                var recipe = _recipeDatabase.GetRecipeByName(recipeName);
                if (recipe != null)
                {
                    unlockedList.Add(recipe);
                }
            }

            return unlockedList;
        }
    }
}
