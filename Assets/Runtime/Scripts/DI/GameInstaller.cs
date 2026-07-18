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

        [Header("Global MonoBehaviour Services (Prefabs)")]
        [Tooltip("Pooled positional SFX prefab. Will be instantiated on launch.")]
        [SerializeField] private SoundManager _soundManagerPrefab;
        [Tooltip("Music & ambient crossfade prefab. Will be instantiated on launch.")]
        [SerializeField] private WorldAudioManager _worldAudioManagerPrefab;
        [Tooltip("VR controller haptics prefab. Will be instantiated on launch.")]
        [SerializeField] private HapticFeedback _hapticFeedbackPrefab;
        [Tooltip("Audio mixer volume control prefab. Will be instantiated on launch.")]
        [SerializeField] private SoundMixerManager _soundMixerManagerPrefab;
        [Tooltip("Floating world-space text/damage-number pool prefab. Will be instantiated on launch.")]
        [SerializeField] private FloatingUIManager _floatingUIManagerPrefab;

        [Header("Other Global Prefabs (No DI)")]
        [Tooltip("Standard Unity/XR prefabs (EventSystem, XR UI Toolkit) to spawn globally. These don't get DI registered, they just live forever.")]
        [SerializeField] private GameObject[] _otherGlobalPrefabs;
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

            // Narrative engine — stack-based dialogue with interruption/resume.
            containerBuilder.RegisterType(typeof(NarrativeService), new[] { typeof(INarrativeService) },
                Lifetime.Singleton, Resolution.Eager);

            // NPC Registry — global so NPCs can register/unregister across scene loads.
            containerBuilder.RegisterType(typeof(NpcRegistryService), new[] { typeof(INpcRegistryService) },
                Lifetime.Singleton, Resolution.Lazy);

            // Dialogue presentation — routes narrative events to NPC audio or 2D fallback.
            containerBuilder.RegisterType(typeof(DialoguePresentationManager), new[] { typeof(DialoguePresentationManager) },
                Lifetime.Singleton, Resolution.Eager);

            // Global game-state machine. Eager so it exists for the whole app lifetime.
            containerBuilder.RegisterType(typeof(GameStateService), new[] { typeof(IGameStateService) },
                Lifetime.Singleton, Resolution.Eager);

            // Event bus for global pub/sub. Eager so it exists for the whole app lifetime.
            containerBuilder.RegisterType(typeof(EventService), new[] { typeof(IEventService) },
                Lifetime.Singleton, Resolution.Eager);

            // Scene orchestrator for VR loading flows. Eager so it can listen/dispatch immediately.
            containerBuilder.RegisterType(typeof(SceneLoadService), new[] { typeof(ISceneLoadService) },
                Lifetime.Singleton, Resolution.Eager);

            // Global MonoBehaviour services — registered as values.
            // Reflex does NOT instantiate the ProjectScope prefab. It only reads from it.
            // Therefore, we must manually Instantiate these prefabs to create real scene objects.
            if (_soundManagerPrefab != null)
            {
                var instance = Instantiate(_soundManagerPrefab);
                DontDestroyOnLoad(instance.gameObject);
                containerBuilder.RegisterValue(instance, new[] { typeof(ISoundService) });
            }
            else
                Debug.LogWarning("GameInstaller: SoundManager prefab is not assigned.");

            if (_worldAudioManagerPrefab != null)
            {
                var instance = Instantiate(_worldAudioManagerPrefab);
                DontDestroyOnLoad(instance.gameObject);
                containerBuilder.RegisterValue(instance, new[] { typeof(IWorldAudioService) });
            }
            else
                Debug.LogWarning("GameInstaller: WorldAudioManager prefab is not assigned.");

            if (_hapticFeedbackPrefab != null)
            {
                var instance = Instantiate(_hapticFeedbackPrefab);
                DontDestroyOnLoad(instance.gameObject);
                containerBuilder.RegisterValue(instance, new[] { typeof(IHapticService) });
            }
            else
                Debug.LogWarning("GameInstaller: HapticFeedback prefab is not assigned.");

            if (_soundMixerManagerPrefab != null)
            {
                var instance = Instantiate(_soundMixerManagerPrefab);
                DontDestroyOnLoad(instance.gameObject);
                containerBuilder.RegisterValue(instance, new[] { typeof(ISoundMixerService) });
            }
            else
                Debug.LogWarning("GameInstaller: SoundMixerManager prefab is not assigned.");
            if (_floatingUIManagerPrefab != null)
            {
                var instance = Instantiate(_floatingUIManagerPrefab);
                DontDestroyOnLoad(instance.gameObject);
                containerBuilder.RegisterValue(instance, new[] { typeof(IFloatingTextService) });
            }
            else
                Debug.LogWarning("GameInstaller: FloatingUIManager prefab is not assigned.");

            // Fade transition — plain C# service, constructor-injected.
            containerBuilder.RegisterType(typeof(FadeTransitionHandler), new[] { typeof(IFadeTransitionService) },
                Lifetime.Singleton, Resolution.Eager);

            // Standard prefabs (UI Toolkit, EventSystem, etc.) that just need to exist globally
            if (_otherGlobalPrefabs != null)
            {
                foreach (var prefab in _otherGlobalPrefabs)
                {
                    if (prefab != null)
                    {
                        var instance = Instantiate(prefab);
                        DontDestroyOnLoad(instance);
                    }
                }
            }
        }
    }
}
