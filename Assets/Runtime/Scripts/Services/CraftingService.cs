using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Plain C# implementation of <see cref="ICraftingService"/>, constructed by Reflex.
    /// Replaces the former <c>CraftingManager</c> singleton: reads/writes the crafting save slice
    /// through <see cref="ISaveService"/> and resolves recipe names against the injected
    /// <see cref="RecipeDatabase"/>. Publishes <see cref="BlueprintUnlockedEvent"/> via EventBus.
    /// </summary>
    public sealed class CraftingService : ICraftingService
    {
        private readonly ISaveService _saveService;
        private readonly RecipeDatabase _recipeDatabase;
        private readonly CraftingSaveData _craftingData;

        public CraftingService(ISaveService saveService, RecipeDatabase recipeDatabase)
        {
            _saveService = saveService;
            _recipeDatabase = recipeDatabase;
            _craftingData = _saveService.CurrentSaveData.craftingData;
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
                _saveService.SaveAsync();

                Debug.Log($"CraftingService: Blueprint Unlocked -> '{recipeName}'");
                EventBus.Publish(new BlueprintUnlockedEvent(recipeName));
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
