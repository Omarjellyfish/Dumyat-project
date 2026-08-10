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
        [Tooltip("Quest database, injected into QuestService.")]
        [SerializeField] private QuestDatabaseSO _questDatabase;
        [Tooltip("Achievement database, injected into AchievementService.")]
        [SerializeField] private AchievementDatabaseSO _achievementDatabase;
        [Tooltip("Menu scene names, loading tips and the controls image, injected into GameSessionService.")]
        [SerializeField] private MenuCatalogSO _menuCatalog;

        [Header("Menus")]
        [Tooltip("Loading overlay PREFAB (asset reference, not instantiated here). LoadingScreenPresenter " +
                 "spawns it on the first scene load and keeps it alive across loads.")]
        [SerializeField] private LoadingScreenView _loadingScreenViewPrefab;

        [Tooltip("Pause menu PREFAB (asset reference, not instantiated here). PauseMenuPresenter spawns " +
                 "one persistent copy on launch, so no scene needs its own. Do NOT also place it in a scene.")]
        [SerializeField] private PauseMenuToolkitUI _pauseMenuPrefab;

        [Tooltip("Notification HUD PREFAB (toast + medal views). Spawned once, on the first notification, " +
                 "and kept across scene loads so every scene can raise one. Do NOT also place it in a scene.")]
        [SerializeField] private NotificationHudManager _notificationHudPrefab;

        [Header("Player")]
        [Tooltip("XR Origin (XR Rig) PREFAB. Spawned by PlayerRigSpawner ONLY into scenes that do not " +
                 "author their own rig, after the previous scene is gone — never alongside an existing " +
                 "one. Scenes with a rig are left untouched.")]
        [SerializeField] private GameObject _playerRigPrefab;

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

        [Header("Debug")]
        [Tooltip("Log routine save activity (loaded / written / reset) to the Console. OFF by default — " +
                 "SaveAsync runs after every inventory mutation, so this is very noisy. Save FAILURES are " +
                 "always logged regardless of this setting.")]
        [SerializeField] private bool _verboseSaveLogging = false;

        [Header("Other Global Prefabs (No DI)")]
        [Tooltip("Standard Unity/XR prefabs (EventSystem, XR UI Toolkit) to spawn globally. These don't get DI registered, they just live forever.")]
        [SerializeField] private GameObject[] _otherGlobalPrefabs;
        public void InstallBindings(ContainerBuilder containerBuilder)
        {
            // Shared save store. Constructed here rather than via RegisterType so the verbose-logging
            // checkbox can be passed in; RegisterValue is still a disposed singleton, and building it
            // now keeps the old Eager behaviour (CurrentSaveData is ready before dependents resolve).
            containerBuilder.RegisterValue(new SaveService(_verboseSaveLogging), new[] { typeof(ISaveService) });

            // Item database asset, resolvable by type for constructor injection.
            if (_itemDatabase != null)
                containerBuilder.RegisterValue(_itemDatabase, new[] { typeof(ItemDatabase) });
            else
                Debug.LogWarning("GameInstaller: ItemDatabase is not assigned; InventoryService will fail to construct.");

            // Inventory — plain C# service constructed from ISaveService + ItemDatabase.
            containerBuilder.RegisterType(typeof(InventoryService), new[] { typeof(IInventoryService) },
                Lifetime.Singleton, Resolution.Lazy);

            // Player EGP balance — constructed from ISaveService + IEventService.
            // EAGER because it self-subscribes to SellScoreEvent: it must exist before the first sale,
            // not lazily on the first UI query, or a sale made before the bag is opened is lost.
            containerBuilder.RegisterType(typeof(WalletService), new[] { typeof(IWalletService) },
                Lifetime.Singleton, Resolution.Eager);

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

            // Quest database asset for QuestService.
            if (_questDatabase != null)
                containerBuilder.RegisterValue(_questDatabase, new[] { typeof(QuestDatabaseSO) });
            else
                Debug.LogWarning("GameInstaller: QuestDatabase is not assigned; QuestService will fail to construct.");

            // Quest tracking — constructed from ISaveService + IEventService + QuestDatabaseSO.
            // Eager so event subscriptions (including SceneLoadCompleteEvent) are live immediately.
            containerBuilder.RegisterType(typeof(QuestService), new[] { typeof(IQuestService) },
                Lifetime.Singleton, Resolution.Eager);

            // Achievement database
            if (_achievementDatabase != null)
                containerBuilder.RegisterValue(_achievementDatabase, new[] { typeof(AchievementDatabaseSO) });
            else
                Debug.LogWarning("GameInstaller: AchievementDatabase is not assigned; AchievementService will fail to construct.");

            // Achievement tracking
            containerBuilder.RegisterType(typeof(AchievementService), new[] { typeof(IAchievementService) },
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

            // Tutorial tracking — persists completion state, manages active sequences.
            containerBuilder.RegisterType(typeof(TutorialService), new[] { typeof(ITutorialService) },
                Lifetime.Singleton, Resolution.Lazy);

            // Menu content (scene names, loading tips, controls image).
            if (_menuCatalog != null)
                containerBuilder.RegisterValue(_menuCatalog, new[] { typeof(MenuCatalogSO) });
            else
                Debug.LogWarning("GameInstaller: MenuCatalog is not assigned; the menus will not be able to change scene.");

            // Loading overlay prefab, registered as an ASSET reference. LoadingScreenPresenter
            // instantiates it lazily; Reflex never touches it.
            if (_loadingScreenViewPrefab != null)
                containerBuilder.RegisterValue(_loadingScreenViewPrefab, new[] { typeof(LoadingScreenView) });
            else
                Debug.LogWarning("GameInstaller: LoadingScreenView prefab is not assigned; scene loads will show nothing.");

            // Player options (audio levels, subtitle mode), persisted to PlayerPrefs.
            // EAGER: saved volumes must reach the mixer and the subtitle mode must be published
            // before the main menu or any caption presenter exists.
            // Microphone / online-AI consent. ROOT rather than the AI scene, because the settings
            // page that now switches it opens from the main menu and the carpentry bench as well as
            // from inside the market — see the note on ConsentManager. Eager so the stored answer is
            // read once at boot instead of on whichever panel asks first.
            containerBuilder.RegisterType(typeof(ConsentManager), new[] { typeof(IConsentService) },
                Lifetime.Singleton, Resolution.Eager);

            containerBuilder.RegisterType(typeof(SettingsService), new[] { typeof(ISettingsService) },
                Lifetime.Singleton, Resolution.Eager);

            // New Game / Continue / Exit.
            // EAGER: it subscribes to SceneManager.sceneLoaded to record the Continue target, which
            // has to be live from the first scene, not from the first time a menu asks.
            containerBuilder.RegisterType(typeof(GameSessionService), new[] { typeof(IGameSessionService) },
                Lifetime.Singleton, Resolution.Eager);

            // Persistent loading overlay. EAGER: it subscribes to SceneLoadBeginEvent, and nothing
            // ever resolves it by hand.
            containerBuilder.RegisterType(typeof(LoadingScreenPresenter), new[] { typeof(ILoadingScreenService) },
                Lifetime.Singleton, Resolution.Eager);

            // Notification HUD prefab, as an ASSET reference. NotificationHudPresenter instantiates it
            // on first use; Reflex never touches it.
            if (_notificationHudPrefab != null)
                containerBuilder.RegisterValue(_notificationHudPrefab, new[] { typeof(NotificationHudManager) });
            else
                Debug.LogWarning("GameInstaller: Notification HUD prefab is not assigned; quest and sale " +
                                 "notifications will not be shown.");

            // The one persistent notification HUD, shared by every scene. LAZY: it must not spawn
            // before a scene with a camera exists, and the first notification is a fine trigger.
            containerBuilder.RegisterType(typeof(NotificationHudPresenter), new[] { typeof(INotificationService) },
                Lifetime.Singleton, Resolution.Lazy);

            // Quest progress toasts / completion medals. EAGER: it subscribes to the quest events, and
            // nothing ever resolves it by hand.
            containerBuilder.RegisterType(typeof(QuestNotificationPresenter), new[] { typeof(QuestNotificationPresenter) },
                Lifetime.Singleton, Resolution.Eager);

            // The XR rig prefab, as an ASSET reference. PlayerRigSpawner instantiates it; Reflex
            // never touches it.
            if (_playerRigPrefab != null)
                containerBuilder.RegisterValue(_playerRigPrefab, new[] { typeof(GameObject) });
            else
                Debug.LogWarning("GameInstaller: Player Rig prefab is not assigned; scenes without their " +
                                 "own XR Origin will load with no camera and no interactors.");

            // Gives rigless scenes a rig on arrival. EAGER: it subscribes to SceneLoadCompleteEvent,
            // and nothing ever resolves it by hand.
            containerBuilder.RegisterType(typeof(PlayerRigSpawner), new[] { typeof(PlayerRigSpawner) },
                Lifetime.Singleton, Resolution.Eager);

            // Pause menu prefab, registered as an ASSET reference for PauseMenuPresenter to spawn.
            if (_pauseMenuPrefab != null)
                containerBuilder.RegisterValue(_pauseMenuPrefab, new[] { typeof(PauseMenuToolkitUI) });
            else
                Debug.LogWarning("GameInstaller: PauseMenu prefab is not assigned; no scene will have a pause menu.");

            // The one persistent pause menu, shared by every scene. EAGER: it must spawn on launch so
            // the input relay on it is live from the first scene; nothing ever resolves it by hand.
            containerBuilder.RegisterType(typeof(PauseMenuPresenter), new[] { typeof(PauseMenuPresenter) },
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

            // Cuts every voice and sound off when a scene is left — see SceneAudioSilencer for why that
            // cannot be left to the callers. EAGER and never resolved by hand: it only has to exist and
            // listen, so a lazy binding would simply never be constructed. Registered here, after the
            // audio services it depends on, so it cannot be built before they are available.
            containerBuilder.RegisterType(typeof(SceneAudioSilencer), new[] { typeof(SceneAudioSilencer) },
                Lifetime.Singleton, Resolution.Eager);

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

            // VR blackout for scene loads. EAGER: it drives itself off SceneLoadBegin/Complete and
            // nothing ever resolves it by hand, so it has to exist before the first load.
            // Handler order against FadeTransitionHandler does not matter — the fade-in only advances
            // on later frames, so that handler's player-placement always lands first either way.
            containerBuilder.RegisterType(typeof(ScreenFadeService), new[] { typeof(IScreenFadeService) },
                Lifetime.Singleton, Resolution.Eager);

            // Travel/door transition — carries the player across the load. Fading is the blackout's
            // job now, not this one's.
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
