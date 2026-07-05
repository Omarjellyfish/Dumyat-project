using System.Collections.Generic;

namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for blueprint/recipe unlock state. Injected in place of
    /// <c>CraftingManager.Singleton</c>. Backed by <see cref="CraftingService"/>.
    /// </summary>
    public interface ICraftingService
    {
        bool IsBlueprintUnlocked(string recipeName);
        void UnlockBlueprint(string recipeName);
        List<CraftingRecipe> GetUnlockedRecipes();
    }
}
