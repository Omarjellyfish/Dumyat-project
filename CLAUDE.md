# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview
Dumyat is a **VR carpentry/crafting game** in **Unity 6 (6000.3.7f1)** using URP and OpenXR
(XR Interaction Toolkit 3.3.1 + XR Hands). Players cut, assemble, and paint wood at crafting
stations in VR.

## Engine, build & run
- Open the project with **Unity Hub / Editor 6000.3.7f1**. There is no CI and no custom build
  scripts — build and run through the Editor (solution: `Dumyat-project.slnx`, Rider/Visual Studio).
- **Run:** open a scene and enter Play mode. The **XR Device Simulator**
  (`Assets/Samples/XR Interaction Toolkit/3.3.1/XR Device Simulator`) lets you play-test without a
  headset.
- **Switch scenes:** use the **Scene Switcher Pro** dropdown on the main toolbar (the `⋮` next to
  the Play buttons in Unity 6.3+). Toggle **"All Scenes"** to list every `.unity`, not just build
  scenes. (`Assets/Editor/Scripts/SceneSwitcherToolbar.cs`)
- **Scenes** live in `Assets/Runtime/Art/Scenes/` (e.g. `El_manara`, `Testing_XR`,
  `TestingCuttingSystem`, `Test_Barzakh_Logic`).
- **Build:** File ▸ Build Profiles/Settings. See Gotchas — the enabled build scene is currently missing.
- **Tests:** none. `com.unity.test-framework` is installed but there are no project test assemblies.
  `ValidationTester` and `CraftingRecipeRecorder` are manual in-Editor dev tools, not automated tests.

## Assemblies & namespaces
Only three assemblies are project code; everything under `Library/PackageCache/` and
`Assets/Samples/` is third-party — do not edit it.
- **`Project.Runtime`** — `Assets/Runtime/`, namespace `DumyatRuntime` — all gameplay code.
- **`Project.Editor`** — `Assets/Editor/` (Editor platform only), namespace `DumyatEditor` — tools.
- **`Project.External`** — `Assets/External/`, namespace `DumyatExternal` — currently empty (reserved).

The **EzySlice** mesh-slicing library is vendored at `Assets/Runtime/Packages/ezy-slice-master`
(compiled into `Project.Runtime`, its own `EzySlice` namespace).

`Project.Runtime.asmdef` references the **`Reflex`** assembly (the DI container, GUID
`1530a967b84cfb44dbdd5e8a1989764f`). Reflex's `autoReferenced` flag only reaches the predefined
`Assembly-CSharp`, so any project asmdef that uses `Reflex.*` types must add that reference explicitly.

## Core architecture

### Managers & services = Reflex DI
Cross-system dependencies are provided by the **Reflex** dependency-injection container
(`com.gustavopsantos.reflex` 14.3.1), **not** by hand-rolled singletons. Consumers depend on
capability **interfaces** in [Assets/Runtime/Scripts/Interfaces/](Assets/Runtime/Scripts/Interfaces/)
(`IInventoryService`, `IHapticService`, `ISoundService`, `ISaveService`, …); concrete
implementations live as plain C# services in
[Assets/Runtime/Scripts/Services/](Assets/Runtime/Scripts/Services/) (POCOs, constructor-injected)
or as scene-bound MonoBehaviours registered as instances.

- **Registration** happens in installers (`IInstaller.InstallBindings(ContainerBuilder)`) in
  [Assets/Runtime/Scripts/DI/](Assets/Runtime/Scripts/DI/):
  - `GameInstaller` → **root** container (app-wide services; formerly the `DontDestroyOnLoad`
    singletons). Lives on the **ProjectScope** prefab whose `ContainerScope` is listed in the
    `Resources/ReflexSettings` asset's *RootScopes*.
  - `SceneInstaller` → **scene** container (services needing the active rig / AudioSource pools /
    coroutines, e.g. `HapticFeedback`). To enable DI in a gameplay scene, drop the
    **`Assets/Runtime/Prefabs/DI/SceneScope.prefab`** (a `ContainerScope` + `SceneInstaller`) into it —
    the installer takes each scene service from its serialized field, or **auto-discovers it in the
    scene** if that field is left empty, so most scenes need no manual wiring.
  - Bind with `RegisterType(typeof(Impl), new[]{ typeof(IContract) }, Lifetime.Singleton,
    Resolution.Lazy)`, `RegisterValue(instance_or_SO, new[]{ typeof(IContract) })`, or
    `RegisterFactory<T>(…)`. Use `Resolution.Eager` for a service that must exist / self-subscribe up front.
- **Consuming:** MonoBehaviours receive dependencies via **method injection** —
  `[Inject] private void Inject(IInventoryService inv, IHapticService haptics) { … }` (cache into
  fields; keep null-guards). Plain C# services use **constructor injection**. Scene MonoBehaviours
  auto-inject on load (a `ContainerScope` in the scene drives it).
- **Runtime-spawned prefabs do NOT auto-inject.** Either add `Reflex.Components.GameObjectSelfInjector`
  to the prefab, or inject manually against the scene container:
  `GameObjectInjector.InjectRecursive(instance, gameObject.scene.GetSceneContainer())` — see
  `ItemSpawner.SpawnItem` and `InventoryWorldUI` slot spawning.

The gameplay singletons have all been retired — there are **no `static Singleton`/`Instance`
accessors and no `IManager` marker** left. Logic-only managers became root POCOs
(`SaveService`/`ISaveService`, `InventoryService`/`IInventoryService`, `CraftingService`/`ICraftingService`,
`PlayerPersistenceService`/`IPlayerPersistenceService`, `GameStateService`/`IGameStateService`);
scene-bound managers stayed MonoBehaviours registered as instances by `SceneInstaller`
(`HapticFeedback`/`IHapticService`, `SoundManager`/`ISoundService`, `WorldAudioManager`/`IWorldAudioService`,
`FloatingUIManager`/`IFloatingTextService`). Always inject the interface; never reintroduce a static
accessor. `GameStateService` owns the `GameState` enum (`MainMenu`/`FreeRoaming`/`Paused`/`InQuest`);
entering `Paused` sets `Time.timeScale = 0`. Still-static (not yet migrated): `SaveManager` (file I/O
behind `SaveService`) and the `EventBus`.

### Save system
`GameSaveData` is one aggregate blob (crafting, inventory, quest, persistence structs) serialized to
`Application.persistentDataPath/dumyat_master_save.json` via `JsonUtility` (async write). Access it
through the injected **`ISaveService`** (`CurrentSaveData` / `Load()` / `SaveAsync()`), implemented by
`SaveService`. Pattern: a service reads its slice from `ISaveService.CurrentSaveData`, mutates it, then
calls `SaveAsync()` — see `InventoryService`. (`SaveService` currently delegates to the static
`SaveManager` for file I/O while the remaining save-backed managers are migrated; new code must use
`ISaveService`, not `SaveManager` directly.)

### Events — EventBus
Cross-system communication goes through **`EventBus`**
([Assets/Runtime/Scripts/Managers/Common/EventBus.cs](Assets/Runtime/Scripts/Managers/Common/EventBus.cs)),
a static, thread-safe generic pub/sub: `EventBus.Subscribe<T>` / `Unsubscribe<T>` / `Publish<T>`,
keyed on the `IGameEvent` marker. Event payloads are small `readonly struct`s implementing
`IGameEvent`, grouped by domain in [Assets/Runtime/Scripts/Events/](Assets/Runtime/Scripts/Events/)
(e.g. `GameStateChangedEvent`, `InventoryChangedEvent`, `ItemDroppedEvent`, `WoodCutEvent`,
`CraftingValidatedEvent`). **This is the standard mechanism — prefer it for every new decoupled
cross-system reaction.** Publish from the owning system; subscribe in `OnEnable` and unsubscribe in
`OnDisable`, passing a cached method group (not a lambda) so `Unsubscribe` matches.

Implementation notes: each event type gets its own statically-resolved `Channel<T>` (no dictionary
lookup, no boxing, no per-handler cast); publishing is allocation-free, and subscription changes made
while an event is dispatching are buffered and applied once dispatch unwinds. `EventBus.Clear()`
drops all handlers; `#if UNITY_EDITOR` exposes `GetSubscriberCounts`/`LogSubscriberCounts`.
Note: XR Interaction Toolkit callbacks (`selectEntered`/`activated` UnityEvents) are wired in the
Inspector and are **not** part of EventBus.

### Event-driven decoupling
Systems react to each other through EventBus rather than hard references — e.g. `InventoryService`
publishes `InventoryChangedEvent`/`ItemDroppedEvent` (the inventory UIs and `ItemSpawner` subscribe),
`GameManager` publishes `GameStateChangedEvent`, `PlayerPersistence` publishes `KeyUnlockedEvent`,
`CraftingValidator` publishes `CraftingValidatedEvent`. Prefer this over polling or reaching across
the scene with `GetComponent`/`Find`.

### Data via ScriptableObjects
Content/config are ScriptableObjects: `ItemData`/`CraftingRecipe` assets with `ItemDatabase`/
`RecipeDatabase` lookups (e.g. `RecipeDatabase.GetRecipeByName`), plus tuning SOs (`SawSettingsSO`,
`NailHammerSettingsSO`, `HapticProfileSO`, `PerformanceBudgetSettingSO`).

### VR interaction systems (`Assets/Runtime/Scripts/`)
- **CuttingSystem** — `WoodSlicer` slices meshes via EzySlice, driven by `SawTip`/`SawBladePoint`.
- **Crafting** — `CraftingStation` + `StructuralPiece`/`DecorationPiece`; validated by
  `Crafting/Validation` (`CraftingValidator`, `AssemblyGraphUtility`).
- **PaintingSystem** — `PaintBrush`/`PenTip` draw onto `WoodPaintSurface` (`LineDrawing`).
- **ConstraintSystem** — `SawConstraint`/`PenConstraint`/`SurfaceConstraint` constrain tool motion.
- Plus **Inventory**, **HapticFeedback**, **Audio** (`SoundManager`/`SoundMixerManager`/
  `WorldAudioManager`), and a UI-Toolkit **Performance Monitor** overlay.
- Interaction contracts: `IInteractable.OnInteract()`, `IStructuralPiece`, `IDecoration`.

## Editor tools (`Assets/Editor/Scripts/`)
Scene Switcher Pro (toolbar), Auto Collider Tool (`AutoColliderWindow` + `ColliderGizmoDrawer`),
`CraftingRecipeRecorder`, `WaterPropertyInjectorEditor`.

## Writing maintainable, reusable code

The guiding goal for all new code here is **small, composable, single-responsibility components** —
behaviour that designers can mix onto GameObjects in the Editor, not large god-classes. When adding
or changing code, prefer the option that keeps pieces decoupled and reusable:

- **One responsibility per `MonoBehaviour`.** A component should do one thing (collect an item,
  play a sound, toggle a ray). Compose behaviours by adding multiple small components to a
  GameObject rather than growing one class. If a class starts handling two concerns, split it. Note
  that even the heavier systems here delegate — e.g. `ItemPickup` only resolves an ID and calls into
  the injected inventory/haptic services; it does not own inventory logic.
- **Program to interfaces, not concrete types.** Extend the marker/contract interfaces in
  [Assets/Runtime/Scripts/Interfaces/](Assets/Runtime/Scripts/Interfaces/)
  ([IInteractable](Assets/Runtime/Scripts/Interfaces/IInteractable.cs),
  [IDecoration](Assets/Runtime/Scripts/Interfaces/IDecoration.cs),
  [IStructuralPiece](Assets/Runtime/Scripts/Interfaces/IStructuralPiece.cs)) and the capability
  service interfaces (`IInventoryService`, `IHapticService`, …) so systems depend on capabilities,
  not specific classes. Add a new interface when a new capability is shared by more than one component.
- **Decouple via Reflex DI and EventBus, not hard references or singletons.** This is the standard
  and it is **required for new features**:
  - **Dependencies →** define a capability interface, register the implementation in an installer
    (`GameInstaller`/`SceneInstaller`), and receive it via `[Inject]` (method injection for
    MonoBehaviours, constructor injection for POCOs). See "Managers & services = Reflex DI".
  - **Cross-system reactions →** publish/subscribe a `readonly struct : IGameEvent` through
    `EventBus` (see the Events section).
  - **Do not** add new `static Singleton`/`Instance` accessors, reach across the scene with
    `GetComponent`/`Find` chains, or wire direct references between unrelated components. A component
    should know only its own GameObject, its injected services, and the events it publishes.
- **Expose data, not behaviour, to designers via ScriptableObjects.** Put tunable values
  (item stats, haptic profiles) in ScriptableObjects (`HapticProfileSO`, `ItemData`) so the same
  component can be reconfigured without code changes or subclasses.
- **Keep components self-contained and null-safe.** Cache injected services in the `[Inject]` method,
  null-check them before use (injection can be absent if a scene lacks a `ContainerScope`, or a
  runtime-spawned prefab was not injected), and fail with a clear `Debug.LogWarning` rather than a
  `NullReferenceException` so a component dropped into a scene without its dependencies degrades
  gracefully.
- **Reuse before you add.** Before writing a new service, audio call, save field, or interaction,
  check whether an existing system already covers it (pooled audio via `ISoundService`, persistence
  via `ISaveService`/`PlayerPersistence`, haptics via `IHapticService`). Extend the shared system
  rather than duplicating it.

## Conventions

- Private serialized fields use `_camelCase` with `[SerializeField]`; expose read-only access via
  properties (e.g. `IReadOnlyList<T>` for collections). This keeps components configurable in the
  Inspector while protecting their internal state — follow it for every new component.
- Editor-only scripts must be wrapped in `#if UNITY_EDITOR` (and live in `Project.Editor` when
  practical).
- Every asset/script has a paired `.meta` file — when moving or deleting files, move/delete the
  `.meta` too so Unity GUID references stay intact.

## Gotchas
- The only **build-enabled scene**, `Assets/Runtime/Art/Scenes/carpentar_room.unity`, is **missing on
  this branch** (a recent commit deleted the "carpentar room in art"; only
  `Assets/Scenes/DemoScenes/carpentar_room_Demo.unity` remains). Fix the Build Settings scene list
  before making a player build.
- When editing, **match the target file's existing namespace** — nearly all runtime code (including
  `EventBus`, `IGameEvent`, and the `Events/` structs) is `DumyatRuntime`.
- Never edit `Library/` or `Assets/Samples/` — generated / third-party.
