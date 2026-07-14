# Dumyat — Architecture & System Integration

How the project is put together, and the exact seams you use to add a new system and
wire it into everything else. For *how each existing system behaves and how to drive it*,
see [SYSTEMS.md](SYSTEMS.md). For the merchant/AI locale + tone rules, see
[MERCHANT_CONTEXT.md](MERCHANT_CONTEXT.md). For the engine/build basics, see the repo
root [CLAUDE.md](../CLAUDE.md).

> **Note on CLAUDE.md drift:** CLAUDE.md still describes cross-system events as a *static*
> `EventBus.Publish/Subscribe`. On this branch that has been replaced by an **injected
> `IEventService`** (implementation `EventService`). The pub/sub design is identical
> (statically-resolved generic channels, zero-alloc dispatch, buffered subscribe/unsubscribe
> during dispatch), but you now receive it via `[Inject]` instead of calling a static. This
> document reflects the code as it actually is.

---

## 1. The big picture

Dumyat is a VR carpentry + fish-market game on **Unity 6 (6000.3.7f1)**, URP, OpenXR
(XR Interaction Toolkit 3.3.1 + XR Hands). Three ideas carry the whole codebase:

1. **Small, single-responsibility MonoBehaviours** composed on GameObjects — not god-classes.
2. **Reflex dependency injection** for capabilities (services), so components depend on
   *interfaces*, never concrete managers or singletons.
3. **`IEventService` pub/sub** for cross-system reactions, so systems never hold hard
   references to each other.

Everything below is a consequence of those three ideas. If you keep them, a new feature
drops in without touching existing code.

```
                       ┌─────────────────────────────────────────────┐
                       │              Reflex DI containers            │
   ProjectScope  ─────▶│  ROOT   (GameInstaller)  app-wide services   │
   (Resources/         │    │                                         │
    ReflexSettings)    │    └── SCENE (SceneInstaller subclasses)     │
                       │              per-scene MonoBehaviour services │
                       └───────────────┬─────────────────────────────┘
                                       │ [Inject]
        ┌──────────────────────────────┼──────────────────────────────┐
        ▼                              ▼                               ▼
   Gameplay MonoBehaviours       POCO services              AI / Merchant pipeline
   (ItemPickup, WoodSlicer,      (InventoryService,         (LLM/TTS/STT managers,
    CraftingStation, ...)         SaveService, ...)          NegotiationFlowController)
        │                              │                               │
        └──────────────► IEventService pub/sub ◄───────────────────────┘
                    (InventoryChangedEvent, WoodCutEvent,
                     CraftingValidatedEvent, FishSaleRecordedEvent, ...)
```

---

## 2. Assemblies & namespaces

Only **three** assemblies are project code. Everything under `Library/PackageCache/` and
`Assets/Samples/` is third-party — **never edit it**.

| Assembly | Location | Namespace | Contents |
|---|---|---|---|
| `Project.Runtime` | `Assets/Runtime/` | `DumyatRuntime` | **All gameplay + AI code.** |
| `Project.Editor` | `Assets/Editor/` | `DumyatEditor` | Editor tools (Editor platform only). |
| `Project.External` | `Assets/External/` | `DumyatExternal` | Reserved, currently empty. |

- **EzySlice** (mesh slicing) is vendored at `Assets/Runtime/Packages/ezy-slice-master`,
  compiled into `Project.Runtime`, in its own `EzySlice` namespace.
- `Project.Runtime.asmdef` explicitly references the **Reflex** assembly (GUID
  `1530a967b84cfb44dbdd5e8a1989764f`). Reflex's `autoReferenced` only reaches the
  predefined `Assembly-CSharp`, so **any asmdef that uses `Reflex.*` must add that reference
  explicitly**.
- When editing a file, **match its existing namespace** — nearly all runtime code is
  `DumyatRuntime`.

---

## 3. Dependency injection (Reflex) — the service layer

Cross-system dependencies are provided by the **Reflex** container
(`com.gustavopsantos.reflex` 14.3.1). There are **no static `Singleton`/`Instance`
accessors** and no `IManager` marker — those were all retired. A component knows only its
own GameObject, its injected services, and the events it publishes.

### 3.1 Two containers

| Container | Installer | Lives on | Lifetime | Use for |
|---|---|---|---|---|
| **Root** | `GameInstaller` | **ProjectScope** prefab (a `ContainerScope`) registered in `Resources/ReflexSettings` → *RootScopes* | whole app | App-wide, logic-only services + `DontDestroyOnLoad` MonoBehaviour services. |
| **Scene** | a `GenericSceneInstaller` subclass | **SceneScope**-style prefab (`ContainerScope` + installer) dropped into the scene | that scene | Services that need the active rig / AudioSource pools / scene objects. |

The scene container is a **child** of the root, so scene code resolves both scene and root
bindings; root code cannot see scene bindings.

### 3.2 Registration — inside `InstallBindings(ContainerBuilder)`

`GameInstaller` ([Assets/Runtime/Scripts/DI/GameInstaller.cs](../Assets/Runtime/Scripts/DI/GameInstaller.cs))
shows every pattern:

```csharp
// POCO service bound to its interface, lazily constructed by Reflex (constructor injection):
builder.RegisterType(typeof(InventoryService), new[] { typeof(IInventoryService) },
    Lifetime.Singleton, Resolution.Lazy);

// Eager: must exist / self-subscribe up front (state machine, event hub, save store):
builder.RegisterType(typeof(EventService), new[] { typeof(IEventService) },
    Lifetime.Singleton, Resolution.Eager);

// A ScriptableObject asset resolvable by type (so POCOs can take it in their ctor):
builder.RegisterValue(_itemDatabase, new[] { typeof(ItemDatabase) });

// A MonoBehaviour service: Reflex does NOT instantiate ProjectScope prefabs, so the
// installer Instantiates the prefab itself, DontDestroyOnLoad's it, then registers the instance:
var instance = Instantiate(_soundManagerPrefab);
DontDestroyOnLoad(instance.gameObject);
builder.RegisterValue(instance, new[] { typeof(ISoundService) });
```

**Root bindings today** (`GameInstaller`): `ISaveService` (eager), `ItemDatabase`,
`IInventoryService`, `RecipeDatabase`, `ICraftingService`, `IPlayerPersistenceService`,
`IGameStateService` (eager), `IEventService` (eager), and the `DontDestroyOnLoad`
MonoBehaviour services `ISoundService`, `IWorldAudioService`, `IHapticService`,
`ISoundMixerService`.

### 3.3 Scene installers — serialized-or-auto-discover

`GenericSceneInstaller`
([Assets/Runtime/Scripts/DI/GenericSceneInstaller.cs](../Assets/Runtime/Scripts/DI/GenericSceneInstaller.cs))
gives you a `Register<TImpl, TContract>(builder, serializedField)` helper: it uses the
serialized instance if assigned, **or finds the first one in the scene** (including
inactive). So most scenes need no manual wiring — drop the scope prefab in and the installer
discovers its services.

Concrete scene installers:
- **`CraftingSceneInstaller`** — registers `IFloatingTextService` (`FloatingUIManager`).
- **`AISceneInstaller`** — registers the whole merchant pipeline: `ILLMService`,
  `ITTSService`, `ISTTService`, `IConsentService`, `IPlayerStatsService`,
  `IMerchantNegotiationService`, `INegotiationService`. (The provider *clients* are not
  registered — see §7.)

### 3.4 Consuming — how a component gets a service

- **MonoBehaviours → method injection.** Cache into fields; keep null-guards.
  ```csharp
  private IInventoryService _inventory;
  private IHapticService _haptics;
  [Inject] private void Inject(IInventoryService inv, IHapticService haptics) {
      _inventory = inv; _haptics = haptics;
  }
  ```
  Scene MonoBehaviours auto-inject on load because a `ContainerScope` is in the scene.

- **POCO services → constructor injection** (see `InventoryService`, `SaveService`).

- **Runtime-spawned prefabs do NOT auto-inject.** Either add
  `Reflex.Components.GameObjectSelfInjector` to the prefab, or inject manually against the
  scene container:
  ```csharp
  GameObjectInjector.InjectRecursive(instance, gameObject.scene.GetSceneContainer());
  ```
  See `ItemSpawner.SpawnItem` and `InventoryWorldUI` slot spawning.

**Null-safety is mandatory.** Injection can be absent (a scene without a `ContainerScope`,
or a spawned prefab that wasn't injected). Null-check services and fail with a clear
`Debug.LogWarning`, never a `NullReferenceException`. Every service call site in the
codebase does this (`_inventory?.` / `if (_tts == null) …`).

---

## 4. Events (`IEventService`) — the reaction layer

Cross-system communication goes through the injected **`IEventService`**
(`Subscribe<T>` / `Unsubscribe<T>` / `Publish<T>`), keyed on the `IGameEvent` marker.
Implementation: [Services/EventService.cs](../Assets/Runtime/Scripts/Services/EventService.cs).

- **Payloads are small `readonly struct`s** implementing `IGameEvent`, grouped by domain in
  [Assets/Runtime/Scripts/Events/](../Assets/Runtime/Scripts/Events/):
  `GameEvents`, `InventoryEvents`, `CuttingEvents`, `CraftingEvents`, `PlayerEvents`,
  `SettingsEvents`.
- **Publish from the owning system; subscribe in `OnEnable`, unsubscribe in `OnDisable`**,
  passing a **cached method group** (not a lambda) so `Unsubscribe` matches.
- Dispatch is allocation-free: each event type `T` gets its own statically-resolved
  `Channel<T>`, so `Publish<T>` is a direct static-field read — no dictionary, no boxing,
  no cast. Subscribe/unsubscribe made *while dispatching* is buffered and applied when
  dispatch unwinds, so the handler list is never mutated mid-iteration.
- `EventService.Clear()` drops all handlers; `#if UNITY_EDITOR` exposes
  `GetSubscriberCounts` / `LogSubscriberCounts`.

```csharp
private IEventService _events;
[Inject] private void Inject(IEventService events) => _events = events;

private void OnEnable()  => _events.Subscribe<InventoryChangedEvent>(OnInventoryChanged);
private void OnDisable() => _events.Unsubscribe<InventoryChangedEvent>(OnInventoryChanged);
private void OnInventoryChanged(InventoryChangedEvent e) { /* react */ }

// publisher side:
_events.Publish(new WoodCutEvent(pos, wood, upper, lower));
```

**Events in the codebase today:** `GameStateChangedEvent`, `GamePauseToggledEvent`,
`InventoryChangedEvent`, `ItemDroppedEvent`, `WoodCutEvent`, `RecipeSelectedEvent`,
`CraftingValidatedEvent`, `BlueprintUnlockedEvent`, `NailAttachedEvent`/`NailRemovedEvent`,
`GlueAppliedEvent`, `AttachmentRemovedEvent`, plus player/settings/AI events
(`KeyUnlockedEvent`, `ConsentChangedEvent`, `FishSaleRecordedEvent`,
`LLMResponseFailedEvent`).

> XR Interaction Toolkit callbacks (`selectEntered` / `activated` UnityEvents) are wired in
> the Inspector and are **not** part of `IEventService`.

**DI vs Events — which to use:** need a *capability* (do X for me) → inject an interface.
Need to *react* to something that happened elsewhere → subscribe to an event. Never reach
across the scene with `GetComponent`/`Find` chains between unrelated systems.

---

## 5. Save system

`GameSaveData` is one aggregate blob (crafting, inventory, quest, persistence structs)
serialized to `Application.persistentDataPath/dumyat_master_save.json` via `JsonUtility`
(async write). Access it **only** through the injected **`ISaveService`**:

```csharp
// pattern used by InventoryService, CraftingService, PlayerPersistenceService:
var slice = _saveService.CurrentSaveData.inventoryData;   // read your slice
slice.items.Add(newItem);                                 // mutate it
_saveService.SaveAsync();                                  // persist the whole blob
```

`CurrentSaveData` lazy-loads on first access; `Load()` reloads from disk; `SaveAsync()`
writes on a background thread. Save slices live in
[Assets/Runtime/Scripts/Data/Save/](../Assets/Runtime/Scripts/Data/Save/).

---

## 6. Data via ScriptableObjects

Content and tuning are ScriptableObjects, so designers reconfigure behaviour without code:

- **Databases:** `ItemDatabase` (`GetItemByID`), `RecipeDatabase` (`GetRecipeByName`) —
  registered as values in `GameInstaller` and injected into services.
- **Item/recipe content:** `ItemData`, `CraftingRecipe`.
- **Tuning SOs:** `SawSettingsSO`, `NailHammerSettingsSO`, `HapticProfileSO`,
  `PerformanceBudgetSettingSO`.
- **Merchant/AI content:** `MerchantConfig` (personality, deception rate, patience, voice +
  ElevenLabs voice params), `MerchantSystemConfig` (roster, cooldown), `FishSpeciesSO`,
  `ElevenLabsConfig` / `GeminiConfig` / `GroqConfig` (API keys, model IDs).

Expose **data, not behaviour** through SOs; expose behaviour through small components.

---

## 7. AI / provider pooling — a second integration pattern

The AI subsystem adds a **provider-pool** pattern on top of DI, so you can add or swap an
LLM/TTS/STT vendor without touching consumers.

```
Consumer  ──[Inject]──▶  ILLMService / ITTSService / ISTTService   (capability interface)
                              │  (implemented by the Manager)
                              ▼
                     LLMManager / TTSManager / STTManager          (AIModelManager<TManager,TModel>)
                              │  owns a priority-sorted pool, auto-fallback
                              ▼
              ILLMModel / ITTSModel / ISTTModel  ◀── auto-discovered in the scene at Start()
                     (GeminiClient, GroqClient, ElevenLabsTTSModel, ElevenLabsScribeClient)
                              │
                              ▼   each model owns its own API key / REST / timeout / auth
                       provider Config SO (ElevenLabsConfig, GeminiConfig, GroqConfig)
```

- `AIModelManager<TManager,TModel>`
  ([AI/Managers/AIModelManager.cs](../Assets/Runtime/Scripts/AI/Managers/AIModelManager.cs))
  **auto-discovers** any scene MonoBehaviour implementing the model interface at `Start()`,
  keeps the pool **sorted by `Priority` (higher first)**, and can `RegisterModel` /
  `UnregisterModel` at runtime. Models never reference the manager (`IAIModel` gives it
  `ModelName` / `IsAvailable` / `Priority`).
- **Fallback strategy differs per modality:**
  - `LLMManager` / `TTSManager` — **active + auto-fallback**: try available models in
    priority order, use the first that succeeds.
  - `STTManager` — **selection-time** fallback only (pick the best available provider when
    listening starts; you can't switch mid-utterance).
- **The managers are the DI services**; the provider *clients* are **not** registered in the
  container — they just sit in the scene and get discovered into the pool. That's why
  `AISceneInstaller` registers the managers but not `GeminiClient`/`ElevenLabsTTSModel`.

---

## 8. How to add a new system (the recipe)

Follow the shape the codebase already uses. Pick the layer that matches what you're adding.

### A) A new gameplay behaviour (component)
1. Write a small MonoBehaviour with **one responsibility**, private `[SerializeField]
   _camelCase` fields, read-only properties for external access.
2. Depend on **interfaces**, not concrete managers. Add an `[Inject]` method; cache +
   null-guard the services.
3. To *react* to another system, subscribe to its event in `OnEnable` / unsubscribe in
   `OnDisable` (cached method group). To *announce* something, define a
   `readonly struct : IGameEvent` in `Events/` and `Publish` it.
4. Put tunable numbers in a ScriptableObject, not literals.
5. If it's spawned at runtime, add `GameObjectSelfInjector` to the prefab (or inject it
   manually) — runtime prefabs don't auto-inject.

### B) A new shared capability (service)
1. Define the capability **interface** in
   [Assets/Runtime/Scripts/Interfaces/](../Assets/Runtime/Scripts/Interfaces/) (grouped by
   domain: `Audio/`, `Core/`, `Crafting/`, `Interaction/`, `Inventory/`, `Save/`, `UI/`,
   `AI/`).
2. Implement it:
   - **Logic-only, no scene state →** a POCO in
     [Services/](../Assets/Runtime/Scripts/Services/) with constructor injection. Register in
     `GameInstaller` with `RegisterType(typeof(Impl), new[]{ typeof(IContract) }, …)`.
   - **Needs the rig / AudioSources / coroutines →** a MonoBehaviour. If app-wide, add it as
     a prefab field on `GameInstaller` and `Instantiate` + `DontDestroyOnLoad` +
     `RegisterValue` it. If scene-scoped, add a serialized field to the relevant
     `GenericSceneInstaller` subclass and register it with the `Register<TImpl,TContract>`
     helper (auto-discovered if left empty).
3. Choose `Resolution.Eager` only if it must exist / self-subscribe up front (state
   machines, the event hub); otherwise `Resolution.Lazy`.
4. Consumers inject the **interface** — never the concrete class, never a static.

### C) A new AI provider (LLM/TTS/STT model)
1. Create a client MonoBehaviour implementing `ILLMModel` / `ITTSModel` / `ISTTModel`
   (which extend `IAIModel`). Own your own API key/REST/timeout; read config from a
   provider `Config` SO.
2. Set a sensible `Priority` and report `IsAvailable` honestly (e.g. false if no API key).
3. Drop it in the AI scene. The manager **auto-discovers** it into the pool — no installer
   or consumer change needed. Higher priority wins; lower priority is the fallback.

### D) Enabling DI in a brand-new scene
Drop the scene-scope prefab (`ContainerScope` + the appropriate `GenericSceneInstaller`
subclass) into the scene. Assign scene services on the installer, or leave them empty to
auto-discover. Root services are already available via the ProjectScope.

---

## 9. Directory map (runtime code)

```
Assets/Runtime/Scripts/
├── DI/               GameInstaller, GenericSceneInstaller, CraftingSceneInstaller, AISceneInstaller
├── Interfaces/       capability contracts, grouped: Audio, Core, Crafting, Interaction,
│                     Inventory, Save, UI, AI  (+ IAIModel/ILLMModel/ITTSModel/ISTTModel)
├── Services/         POCO root services: Save, Inventory, Crafting, PlayerPersistence,
│                     GameState, Event
├── Events/           readonly struct : IGameEvent payloads, grouped by domain
├── Managers/         scene/global MonoBehaviour services (Audio/*, Common/ItemSpawner)
├── Data/             ScriptableObjects/ (databases, tuning) + Save/ (save slices)
├── Systems/          gameplay: CuttingSystem, Crafting, PaintingSystem, ConstraintSystem,
│                     Inventory, HapticFeedback
├── AI/               Managers (LLM/TTS/STT/AIModelManager), provider clients
│                     (Gemini, Groq, ElevenLabs), Voice capture, Config SOs
├── Merchant/         MerchantNPC, MerchantSpawner, NegotiationFlowController,
│                     PriceNegotiationCalculator, Data/ (MerchantConfig, NegotiationState, ...)
├── UI/               Crafting/Inventory UIs, FloatingText, Pause/Settings/Menu managers
├── Player/           XR setup scripts
└── Utilities/        PerformanceMonitor, VFX/shader injectors, zone handlers
```

See [SYSTEMS.md](SYSTEMS.md) for how each of these behaves and how to use it.
