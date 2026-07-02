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

## Core architecture

### Managers = singletons
Gameplay managers are MonoBehaviours exposing `public static T Singleton { get; private set; }`, set
in `Awake` with a destroy-duplicate guard (`GameManager` also calls `DontDestroyOnLoad`). They
implement the empty `IManager` marker. Access via `GameManager.Singleton`, `CraftingManager.Singleton`,
etc. **The property is `Singleton`, not `Instance`.** `GameManager` owns a `GameState` enum
(`MainMenu`/`FreeRoaming`/`Paused`/`InQuest`); entering `Paused` sets `Time.timeScale = 0`.

### Save system
`SaveManager` (a **static** class, not a MonoBehaviour) serializes one aggregate `GameSaveData` to
`Application.persistentDataPath/dumyat_master_save.json` via `JsonUtility` (async write). `GameSaveData`
holds per-system save structs (crafting, inventory, quest, persistence). Pattern: a manager reads its
slice from `SaveManager.CurrentSaveData`, mutates it, then calls `SaveManager.SaveGameAsync()` — see
`CraftingManager.UnlockBlueprint`.

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
Systems react to each other through EventBus rather than hard references — e.g. `InventoryManager`
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
  the inventory/haptic singletons; it does not own inventory logic.
- **Program to interfaces, not concrete types.** Extend the marker/contract interfaces in
  [Assets/Runtime/Scripts/Interfaces/](Assets/Runtime/Scripts/Interfaces/)
  ([IInteractable](Assets/Runtime/Scripts/Interfaces/IInteractable.cs),
  [IDecoration](Assets/Runtime/Scripts/Interfaces/IDecoration.cs),
  [IStructuralPiece](Assets/Runtime/Scripts/Interfaces/IStructuralPiece.cs),
  [IManager](Assets/Runtime/Scripts/Interfaces/IManager.cs)) so systems depend on capabilities, not
  specific classes. Add a new interface when a new capability is shared by more than one component.
- **Decouple via events and the singleton API, not hard references.** Cross-system reactions should
  go through `EventBus` events (see the Events section) or `Manager.Singleton.Method(...)` calls.
  Avoid wiring direct references between unrelated components
  or reaching across the scene with `GetComponent`/`Find` chains; a component should only know about
  its own GameObject and the manager APIs.
- **Expose data, not behaviour, to designers via ScriptableObjects.** Put tunable values
  (item stats, haptic profiles) in ScriptableObjects (`HapticProfileSO`, `ItemData`) so the same
  component can be reconfigured without code changes or subclasses.
- **Keep components self-contained and null-safe.** Resolve dependencies in `Awake`/`Start`,
  null-check `Singleton` access (the existing code always does — keep it), and fail with a clear
  `Debug.LogWarning` rather than a `NullReferenceException` so a component dropped into a scene
  without its dependencies degrades gracefully.
- **Reuse before you add.** Before writing a new manager, audio call, save field, or interaction,
  check whether an existing system already covers it (pooled audio via `SoundManager`, persistence
  via `SaveManager`/`PlayerPersistence`, haptics via `HapticFeedback`). Extend the shared system
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
