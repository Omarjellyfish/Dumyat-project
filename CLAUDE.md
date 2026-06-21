# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

Dumyat is a **VR game** built in **Unity 6000.3.7f1 (Unity 6.3)** targeting OpenXR. It uses the
Universal Render Pipeline (URP) and the XR Interaction Toolkit 3.3.1 / XR Hands stack. Design docs
(GDD, story, guidelines) are linked in [README.md](README.md) — there is no written spec in-repo.

## Build / run / test

There is no CLI build script — development is driven through the Unity Editor.

- **Open the project:** open the folder in Unity Hub with editor `6000.3.7f1`. The exact version
  matters; XR + URP packages are pinned in [Packages/manifest.json](Packages/manifest.json).
- **Run:** enter Play mode in the Editor. Builds are produced via *File > Build Profiles*.
- **Switch scenes fast:** a custom editor toolbar ("Scene Switcher Pro", see
  [SceneSwitcherToolbar.cs](Assets/Editor/Scripts/SceneSwitcherToolbar.cs)) adds a scene dropdown to
  the main toolbar (under the ⋮ menu near Play in 6.3+). Toggle "All Scenes" to list every `.unity`
  file vs. only build-settings scenes.
- **Tests:** the Unity Test Framework (`com.unity.test-framework`) is installed but no test
  assemblies or tests exist yet. Run any future tests via *Window > General > Test Runner*.

C# code is compiled by Unity, not by `dotnet`. The `.csproj`/`.slnx` files are Unity-generated — do
not hand-edit them; edit `.asmdef` files instead.

## Assembly structure

Project code lives under `Assets/` and is split into three assemblies (everything else under
`Assets/Samples/` is vendored XR sample code — avoid editing it):

| Assembly | Path | Namespace | Notes |
|----------|------|-----------|-------|
| `Project.Runtime` | [Assets/Runtime/](Assets/Runtime/) | `DumyatRuntime` | All gameplay code lives here |
| `Project.Editor` | [Assets/Editor/](Assets/Editor/) | `DumyatEditor` | Editor-only tooling; references Runtime |
| `Project.External` | [Assets/External/](Assets/External/) | `DumyatExternal` | Third-party / glue code |

When adding runtime gameplay, put it under `Assets/Runtime/Scripts/` in the `DumyatRuntime`
namespace. If you add a new package/assembly reference, add its GUID to the relevant `.asmdef`.

## Architecture

### Singleton managers
Cross-cutting systems are `MonoBehaviour` singletons exposing a static `Singleton` property, set in
`Awake()` with the destroy-duplicate + `DontDestroyOnLoad` pattern. Examples:
[GameManager](Assets/Runtime/Scripts/Managers/Common/GameManager.cs),
[InventoryManager](Assets/Runtime/Scripts/Managers/Inventory/InventoryManager.cs),
[SoundManager](Assets/Runtime/Scripts/Managers/Audio/SoundManager.cs),
[HapticFeedback](Assets/Runtime/Scripts/HapticFeedback/HapticFeedback.cs),
[PlayerPersistence](Assets/Runtime/Scripts/Player/PlayerPersistence.cs).
Manager classes implement the (currently empty) marker interface
[IManager](Assets/Runtime/Scripts/Interfaces/IManager.cs). Access other systems via
`OtherManager.Singleton.Method(...)` after null-checking `Singleton`.

### Centralized save system
[SaveManager](Assets/Runtime/Scripts/Managers/Save/SaveManager.cs) is a **static** class (no
MonoBehaviour) that owns a single `GameSaveData` written as one JSON file
(`dumyat_master_save.json`) to `Application.persistentDataPath`, via `JsonUtility` and an async
file write.
[GameSaveData](Assets/Runtime/Scripts/Data/Save/GameSaveData.cs) aggregates per-domain serializable
sub-objects (`InventorySaveData`, `QuestSaveData`, `PersistenceSaveData`).

The key pattern: a manager **does not hold its own copy** of state — in `Awake()` it ensures
`SaveManager.CurrentSaveData` is loaded, then points its local field at the relevant sub-object
(e.g. `_currentInventory = SaveManager.CurrentSaveData.inventoryData`). Mutating that reference
mutates the master save; the manager then calls `SaveManager.SaveGameAsync()` to persist. Follow
this when adding a new persisted domain: add a `[Serializable]` field to `GameSaveData` and link it
from the owning manager.

### Event-driven decoupling
Systems communicate through `static Action<...>` events rather than direct references where
possible — e.g. `InventoryManager.OnInventoryChanged` / `OnItemDropped`,
`GameManager.OnGameStateChanged`, `PlayerPersistence.OnKeyUnlocked`. UI and other listeners
subscribe to these. Prefer this over polling or hard references for cross-system reactions.

### Game state
[GameManager](Assets/Runtime/Scripts/Managers/Common/GameManager.cs) holds a `GameState` enum
(`MainMenu`, `FreeRoaming`, `Paused`, `InQuest`). `SetCurrentState` applies global side effects
(e.g. `Time.timeScale = 0` on `Paused`) and fires `OnGameStateChanged`.

### ScriptableObject data
Designer-authored data uses ScriptableObjects, created via the `Dumyat/...` *CreateAssetMenu* path
(e.g. [ItemDatabase](Assets/Runtime/Scripts/ScriptableObjects/ItemDatabase.cs) — menu
`Dumyat/Inventory/Database`, plus `ItemData`, `HapticProfileSO`). Items are looked up by string
`ItemID` through `ItemDatabase.GetItemByID`.

### XR conventions
- Haptics go through `HapticFeedback.Singleton.TriggerVibration(...)` with a `HapticHand`
  (`Left`/`Right`/`Both`); prefer a `HapticProfileSO` over raw amplitude/duration/frequency values.
- Code that touches XR Interaction Toolkit types from otherwise-toolkit-agnostic scripts is guarded
  with `#if XR_INTERACTION_TOOLKIT` (see [ItemPickup.cs](Assets/Runtime/Scripts/Inventory/ItemPickup.cs)).
- Audio SFX is pooled — use `SoundManager.Singleton.PlaySoundFxAtPosition(...)`; it manages an
  `ObjectPool<AudioSource>` internally rather than instantiating sources directly.

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
  go through the `static Action<...>` events (see the Event-driven section) or
  `Manager.Singleton.Method(...)` calls. Avoid wiring direct references between unrelated components
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
