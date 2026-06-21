using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "RecipeDatabase", menuName = "Dumyat/Crafting/Database")]
    public class RecipeDatabase : ScriptableObject
    {
        [SerializeField] private List<CraftingRecipe> _allRecipes = new List<CraftingRecipe>();

        public IReadOnlyList<CraftingRecipe> AllRecipes => _allRecipes;

        public CraftingRecipe GetRecipeByName(string recipeName)
        {
            foreach (var recipe in _allRecipes)
            {
                if (recipe != null && recipe.RecipeName == recipeName) return recipe;
            }

            Debug.LogWarning($"Recipe '{recipeName}' not found in the database!");
            return null;
        }
    }
}
