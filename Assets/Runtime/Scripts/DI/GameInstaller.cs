using Reflex.Core;
using Reflex.Enums;
using UnityEngine;
// Disambiguate from UnityEngine.Resolution (the screen-resolution struct).
using Resolution = Reflex.Enums.Resolution;

namespace DumyatRuntime
{
    /// <summary>
    /// Root-container installer for project-wide services (the former "persistent singletons").
    /// Place this on the ProjectScope prefab (a GameObject with a <see cref="ContainerScope"/>) and
    /// register that prefab in the ReflexSettings asset's RootScopes list so these bindings live for
    /// the whole app. See CLAUDE.md ▸ "Managers &amp; services = Reflex DI".
    /// </summary>
    public sealed class GameInstaller : MonoBehaviour, IInstaller
    {
        [Header("Data Assets")]
        [Tooltip("Central item database, injected into InventoryService.")]
        [SerializeField] private ItemDatabase _itemDatabase;
        [Tooltip("Recipe database, injected into CraftingService.")]
        [SerializeField] private RecipeDatabase _recipeDatabase;

        public void InstallBindings(ContainerBuilder containerBuilder)
        {
            // Shared save store. Eager so CurrentSaveData is loaded before dependents resolve.
            containerBuilder.RegisterType(typeof(SaveService), new[] { typeof(ISaveService) },
                Lifetime.Singleton, Resolution.Eager);

            // Item database asset, resolvable by type for constructor injection.
            if (_itemDatabase != null)
                containerBuilder.RegisterValue(_itemDatabase, new[] { typeof(ItemDatabase) });
            else
                Debug.LogWarning("GameInstaller: ItemDatabase is not assigned; InventoryService will fail to construct.");

            // Inventory — plain C# service constructed from ISaveService + ItemDatabase.
            containerBuilder.RegisterType(typeof(InventoryService), new[] { typeof(IInventoryService) },
                Lifetime.Singleton, Resolution.Lazy);

            // Recipe database asset for CraftingService.
            if (_recipeDatabase != null)
                containerBuilder.RegisterValue(_recipeDatabase, new[] { typeof(RecipeDatabase) });
            else
                Debug.LogWarning("GameInstaller: RecipeDatabase is not assigned; CraftingService will fail to construct.");

            // Crafting blueprint unlocks — constructed from ISaveService + RecipeDatabase.
            containerBuilder.RegisterType(typeof(CraftingService), new[] { typeof(ICraftingService) },
                Lifetime.Singleton, Resolution.Lazy);

            // Player persistence keys — constructed from ISaveService.
            containerBuilder.RegisterType(typeof(PlayerPersistenceService), new[] { typeof(IPlayerPersistenceService) },
                Lifetime.Singleton, Resolution.Lazy);

            // Global game-state machine. Eager so it exists for the whole app lifetime.
            containerBuilder.RegisterType(typeof(GameStateService), new[] { typeof(IGameStateService) },
                Lifetime.Singleton, Resolution.Eager);

            // Event bus for global pub/sub. Eager so it exists for the whole app lifetime.
            containerBuilder.RegisterType(typeof(EventService), new[] { typeof(IEventService) },
                Lifetime.Singleton, Resolution.Eager);
        }
    }
}
