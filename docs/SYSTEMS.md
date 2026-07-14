# Dumyat — Systems Reference

How each individual system works and how to use it. For the shared architecture (DI,
events, save) and the recipe for *adding* a new system, read
[ARCHITECTURE.md](ARCHITECTURE.md) first. For merchant locale/tone rules, see
[MERCHANT_CONTEXT.md](MERCHANT_CONTEXT.md).

Every system here follows the same contract: **inject interfaces, publish/subscribe events,
null-guard your services.** Each section below gives *what it does*, *key
files/API*, and *how to use it*.

## Contents
- Core services: [Save](#save-service) · [Inventory](#inventory-service) ·
  [Crafting unlocks](#crafting-service) · [Player persistence](#player-persistence) ·
  [Game state](#game-state-service) · [Events](#event-service)
- Interaction & feedback: [Haptics](#haptics) · [Audio](#audio) · [Floating text](#floating-text)
- VR gameplay: [Cutting](#cutting-system) · [Crafting stations](#crafting-station--validation) ·
  [Attachment tools](#attachment-tools) · [Painting](#painting-system) ·
  [Constraints](#constraint-system) · [Item pickup/spawn](#item-pickup--spawning)
- AI merchant pipeline: [Overview](#ai-merchant-negotiation--overview) ·
  [Providers/pool](#ai-provider-pool-ll--tts--stt) · [Voice capture](#voice-capture) ·
  [Negotiation flow](#negotiation-flow-controller) · [Negotiation LLM](#merchant-negotiation-llm) ·
  [Pricing](#price-negotiation-calculator) · [Merchant spawner/NPC](#merchant-spawner--npc)
- [Utilities](#utilities)

---

# Core services

These are injected capabilities. Depend on the **interface**, never the concrete class.

## Save service
**Interface** `ISaveService` · **impl** `SaveService` · root (eager).

One master JSON blob (`GameSaveData`) at
`Application.persistentDataPath/dumyat_master_save.json`, written async.

```csharp
private ISaveService _save;
[Inject] private void Inject(ISaveService save) => _save = save;

// read your slice → mutate → persist the whole blob:
var inv = _save.CurrentSaveData.inventoryData;
inv.items.Clear();
_save.SaveAsync();
```
`CurrentSaveData` lazy-loads on first access. Don't touch `SaveManager` directly — it's the
static file-I/O backend `SaveService` delegates to while remaining managers migrate.

## Inventory service
**Interface** `IInventoryService` · **impl** `InventoryService` (POCO) · root.

Owns the player's items (persisted through `ISaveService`), resolves IDs through the
injected `ItemDatabase`, and announces changes via events.

```csharp
_inventory.AddItemToInventory("plank_oak", 2);   // publishes InventoryChangedEvent
_inventory.DropItem("plank_oak", 1);             // publishes ItemDroppedEvent + InventoryChangedEvent
var items = _inventory.CurrentItems;             // IReadOnlyList<InventoryItem>
```
`DropItem` fires `ItemDroppedEvent` which `ItemSpawner` listens for to physically spawn the
world asset — inventory logic never touches the scene itself.

## Crafting service
**Interface** `ICraftingService` · **impl** `CraftingService` (POCO) · root.
Tracks unlocked blueprints/recipes (from `RecipeDatabase`, persisted via `ISaveService`),
publishes `BlueprintUnlockedEvent`. This is *unlock/progression* state — distinct from the
in-scene `CraftingStation` (below) that does the actual building.

## Player persistence
**Interface** `IPlayerPersistenceService` · **impl** `PlayerPersistenceService` · root.
Player-level persistent flags (e.g. unlocked keys); publishes `KeyUnlockedEvent`.

## Game state service
**Interface** `IGameStateService` · **impl** `GameStateService` · root (eager).
Owns the `GameState` enum (`MainMenu` / `FreeRoaming` / `Paused` / `InQuest`). Entering
`Paused` sets `Time.timeScale = 0`. Publishes `GameStateChangedEvent`. Inject it to read or
change state; subscribe to the event to react.

## Event service
**Interface** `IEventService` · **impl** `EventService` · root (eager).
The pub/sub hub — see [ARCHITECTURE.md §4](ARCHITECTURE.md#4-events-ieventservice--the-reaction-layer)
for the full API and the subscribe-in-`OnEnable` / cached-method-group rule.

---

# Interaction & feedback

## Haptics
**Interface** `IHapticService` · **impl** `HapticFeedback` (scene MonoBehaviour — needs the
active rig's controller impulse players).

```csharp
_haptics?.TriggerVibration(0.4f, 0.1f, 0.5f, HapticHand.Both);   // amplitude, duration, freq, hand
_haptics?.TriggerVibration(hapticProfileSO, HapticHand.Right);   // designer-authored profile
_haptics?.StartContinuousVibration(0.3f, 0.5f, HapticHand.Left); // e.g. while sawing
_haptics?.StopContinuousVibration(HapticHand.Left);
```
Author reusable feel with `HapticProfileSO` rather than hardcoding numbers. `HapticHand` is
`Left`/`Right`/`Both`.

## Audio
Three separate capabilities, all root MonoBehaviour services:

- **`ISoundService`** (`SoundManager`) — pooled positional SFX & voice lines:
  ```csharp
  _sound?.PlaySoundFxAtPosition(clip, atTransform, volume);
  _sound?.PlayRandomSoundFxAtPosition(clips, atTransform, volume);   // e.g. CraftingStation spawn
  _sound?.PlayLoopingSoundFxAtPosition(clip, atTransform, volume);
  _sound?.PlayVoiceLine(clip, atTransform, volume);
  ```
- **`IWorldAudioService`** (`WorldAudioManager`) — music/ambient crossfades. Drive ambient
  zones with `AmbientZoneTrigger` components.
- **`ISoundMixerService`** (`SoundMixerManager`) — AudioMixer group volumes (settings UI).
- `CollisionAudio` is a per-object component that plays impact SFX on collision (the cutting
  system copies it onto generated pieces so cut wood still sounds right).

## Floating text
**Interface** `IFloatingTextService` · **impl** `FloatingUIManager` · **scene** service
(registered by `CraftingSceneInstaller`). Spawns short-lived world-space labels
(`FloatingText`) — used for pickups, validation results, etc.

---

# VR gameplay

## Cutting system
**Files:** `Systems/CuttingSystem/` — `WoodSlicer`, `SawTip`, `SawBladePoint`.

`WoodSlicer.PerformCut(hitPoints, sawDirection, wood)` slices a wood GameObject with
**EzySlice** along the plane derived from the blade reference transform, then for each of the
two hulls it:
- recenters the mesh pivot (optional), adds a convex `MeshCollider` + `Rigidbody` +
  `XRGrabInteractable` (so pieces are immediately grabbable),
- adds a `StructuralPiece` (so cut pieces are craftable) and copies `CollisionAudio` from the
  original,
- copies the paint `RenderTexture` onto the new `WoodPaintSurface` (paint survives the cut),
- **publishes `WoodCutEvent(position, original, upper, lower)`** and destroys the original.

`SawTip`/`SawBladePoint` feed the contact points and blade direction during a saw stroke.
To react to cuts (VFX, score, tutorial), subscribe to `WoodCutEvent`.

## Crafting station & validation
**Files:** `Systems/Crafting/` — `CraftingStation`, `StructuralPiece`, `DecorationPiece`,
`Validation/CraftingValidator`, `Validation/AssemblyGraphUtility`.

`CraftingStation` (scene MonoBehaviour, injects `ISoundService` + `IEventService`):
- `SelectRecipe(recipe)` spawns the recipe's `RootPrefab` at the spawn point (+ optional
  hologram guide), configures its `Rigidbody`, plays a spawn SFX, and publishes
  `RecipeSelectedEvent`.
- `CheckBuild()` calls `CraftingValidator.ValidateBuild(rootBody, recipe, eventService)`,
  which walks the assembly graph (`AssemblyGraphUtility`) of connected `StructuralPiece`s and
  publishes a **`CraftingValidatedEvent(ValidationResult)`**. UI (`CraftingValidationUI`,
  `CraftingDiagnosticItemUI`) subscribes to show pass/fail.

`IStructuralPiece` / `IDecoration` are the interaction contracts; `CraftingRecipe` (SO)
defines the target build.

## Attachment tools
**Files:** `Systems/Crafting/Attachment/` — `NailHammer`, `GlueBottle`/`GlueBlop`,
`CrowbarTool`.

These join/detach `StructuralPiece`s and each publish an event so the rest of the game can
react: `NailHammer` → `NailAttachedEvent` / `NailRemovedEvent`; `GlueBottle` →
`GlueAppliedEvent`; `CrowbarTool` → `AttachmentRemovedEvent`. Tuning lives in
`NailHammerSettingsSO`.

## Painting system
**Files:** `Systems/PaintingSystem/` — `PaintBrush`, `PenTip`, `WoodPaintSurface`,
`LineDrawing`, `PaintingManager`.

`WoodPaintSurface` owns a per-object paint `RenderTexture`. `PaintBrush.PaintAt(surface,
uv, color, size)` stamps the brush texture into that RenderTexture at the hit UV (GL
immediate-mode blit). `PenTip` supplies the contact UV; `LineDrawing` interpolates strokes.
Because paint is a RenderTexture on the surface, it's copied to child pieces when wood is cut
(`WoodPaintSurface.CopyPaintFrom`).

## Constraint system
**Files:** `Systems/ConstraintSystem/` — `SawConstraint`, `PenConstraint`,
`SurfaceConstraint`. Constrain a tool's motion (e.g. keep the saw on its cutting axis, keep
the pen on the surface) for believable VR tool handling. Add the relevant constraint
component to the tool.

## Item pickup & spawning
**Files:** `Systems/Inventory/ItemPickup.cs`, `Managers/Common/ItemSpawner.cs`.

- `ItemPickup` (injects `IInventoryService` + `IHapticService`) has an `ItemID` + `Amount`.
  Call `Collect()` (from an XR interaction) → adds to inventory, buzzes the controllers,
  destroys itself. Note it *delegates* — it owns no inventory logic.
- `ItemSpawner` subscribes to `ItemDroppedEvent` and instantiates the world asset. **It
  injects spawned prefabs against the scene container** (`GameObjectInjector.InjectRecursive`)
  because runtime-spawned objects don't auto-inject — copy this pattern whenever you spawn a
  prefab that needs services.

---

# AI merchant negotiation

The flagship feature of this branch: a fisherman (the player) haggles, **by voice, in
Egyptian Arabic**, to sell fish to an NPC merchant at the El Borg fish market. Locale and
tone rules are binding — read [MERCHANT_CONTEXT.md](MERCHANT_CONTEXT.md) before touching any
dialogue/voice code.

## AI merchant negotiation — overview

```
MerchantSpawner ── picks who/when ──▶ MerchantNPC ──▶ NegotiationFlowController (INegotiationService)
                                                          │  orchestrates one sale end-to-end
             ┌────────────────────────┬───────────────────┼───────────────────┬──────────────┐
             ▼                        ▼                    ▼                   ▼              ▼
        IConsentService        VoiceCapture         IMerchantNegotiation   ITTSService   IPlayerStats
        (mic+net gate)         Controller (STT)     Service (the LLM)      (speak)       Service (record sale)
                                    │                     │
                              ISTTService           PriceNegotiationCalculator
                              (transcribe)          (deterministic offer/ceiling/deception)
```

**Authority split (deliberate):** deterministic C# owns the numbers (opening offer,
ceiling, deception roll, success score); the LLM owns only per-turn *dialogue* and a
per-turn *action*, and any number it proposes is clamped to the merchant's real economics
before the game trusts it. The model can never go off-rails into a bad sale.

Everything is wired by **`AISceneInstaller`** (drop it + a `ContainerScope` into the scene).
`MerchantAiSceneSetup` (editor tool) helps scaffold the scene.

## AI provider pool (LLM / TTS / STT)
See [ARCHITECTURE.md §7](ARCHITECTURE.md#7-ai--provider-pooling--a-second-integration-pattern)
for the pattern. Consumer-facing capabilities:

- **`ILLMService`** (`LLMManager`) — `GenerateTextAsync(prompt, jsonObject)`; pools
  `ILLMModel`s (`GeminiClient`, `GroqClient`), active + auto-fallback by priority.
  `jsonObject:true` forces provider structured-output mode.
- **`ITTSService`** (`TTSManager`) — `PlaySpeech(voiceId, text, source, onComplete, onError,
  voiceSettings)`; pools `ITTSModel`s (`ElevenLabsTTSModel`), non-blocking playback, active +
  auto-fallback. `ActiveModelName` tells the LLM which engine will read its output.
- **`ISTTService`** (`STTManager`) — `StartListening()` / `StopListening()`, events
  `OnTranscriptionComplete` / `OnTranscriptionFailed`; pools `ISTTModel`s
  (`ElevenLabsScribeClient`), selection-time fallback. `LanguageCode` **must** be set/tested
  for Egyptian dialect, not generic Arabic/MSA.

Provider config (API keys, model IDs, voice IDs) lives in `ElevenLabsConfig` / `GeminiConfig`
/ `GroqConfig` SOs. **To add a provider:** implement the model interface on a scene
MonoBehaviour with a `Priority`; the manager auto-discovers it — no consumer changes.

## Voice capture
**File:** `AI/Voice/VoiceCaptureController.cs`. The single home for capture control, shared by
the merchant flow and the STT test harness. Drives an `IVoiceCaptureSource`
(`ElevenLabsScribeClient`).

- **Session vs capture:** `OpenSession()`/`CloseSession()` own the persistent transport;
  `BeginCapture()`/`EndCapture()` arm/disarm one utterance within the session. Results:
  `OnTranscription` / `OnCaptureFailed`.
- **Two modes** via `AutoReArm`: *continuous* (harness — keep listening) vs *turn-based*
  (negotiation — one utterance per `BeginCapture`, then stop so the mic is off while the
  merchant thinks/speaks). The negotiation flow sets `AutoReArm = false`.
- Capture mode is push-to-talk or voice-activity; it writes an Arabic-safe UTF-8 debug log of
  the whole conversation via `AppendLog(...)`.

## Negotiation flow controller
**Interface** `INegotiationService` · **impl** `NegotiationFlowController` · scene.
The most stateful script in the game — written for **clarity and defensive recovery**: every
external call (STT/LLM/TTS) has a failure path that keeps the player moving, and every state
transition logs with a `[Negotiation]` prefix.

Entry point `BeginNegotiation(merchant)` (called by the spawner):
1. Resolve the fish to sell (`IFishInventory`, else the Inspector debug fish).
2. **Consent + connectivity gate** (`IConsentService`): if voice mode is unavailable it
   prompts for consent (first time) or hands to the scripted fallback.
3. **Opening:** `PriceNegotiationCalculator` fixes the whole envelope (opening offer,
   ceiling, per-turn raise cap, deception tone) from one roll; the LLM only *phrases* the
   opening. Subtitle shows immediately; audio plays in parallel; then it listens.
4. **Turn loop:** listen → transcribe → `IMerchantNegotiationService.ProcessPlayerTurn` →
   apply the validated action (raise / hold / clarify / accept / walk / confirm) → speak →
   listen again, until a terminal action.
5. **Resolution:** on `Accept`, build a `FishSaleRecord`, `IPlayerStatsService.RecordSale`,
   consume the fish from inventory, then tear down and notify the spawner (which releases its
   lock + starts the cooldown).

**Two execution paths:** a **Unity Behavior graph** if one is assigned (its nodes call the
`Bt_*` step methods on the controller), otherwise the **legacy coded flow** (same logic). So
the game works before the graph is wired.

**Recovery is layered:** blank/glitch captures → stay silent and re-listen (the merchant
never speaks before the player does); recoverable STT/LLM glitch → a generic "say that
again" line then re-listen (capped); connectivity/consent lost → scripted fallback. Many
STUB seams (`ShowSubtitle`, `ShowThinkingCue`, `TriggerScriptedFallback`) are where the UI /
scripted-dialogue teams hook in.

## Merchant negotiation LLM
**Interface** `IMerchantNegotiationService` · **impl** `MerchantNegotiationLLM` · scene.
Wraps the pooled `ILLMService` to turn one player utterance into a **strict JSON**
`MerchantTurnResult` (Arabic speech + English subtitle + action + validated numbers).
Injects `ILLMService`, `ITTSService`, `IEventService`.

- `GenerateOpeningLine(state, isDeceptive, withGreeting)` and
  `ProcessPlayerTurn(state, playerText, isDeceptive)` — both build a prompt, request
  structured JSON, and **parse + validate** it; a two-attempt retry (the 2nd feeds back *why*
  the 1st was rejected) recovers malformed/off-rails output. Unvalidated output never drives
  the sale.
- **On rails by design:** deception is passed in (never decided by the model); any proposed
  offer is clamped to a ratchet (offers only go UP, never above the ceiling, never above what
  the player asked, never more than the per-turn raise cap) or the turn is rejected/coerced to
  a legal action with a matching deterministic line.
- **`MerchantAction`** the model can declare: `RaiseOffer`, `HoldFirm`, `Accept`, `WalkAway`,
  `ClarifyRequest`, plus the confirmation-gated `ConfirmDecline`→`PlayerDeclined` and
  `ConfirmAccept`→`PlayerAccepted` (a destructive end always requires a one-turn confirm, so a
  mis-transcription can't end the sale). Accept/decline are driven by thresholding the model's
  reported `AcceptanceScore` / `RejectionScore` measurements, not by letting the model choose
  to close.
- **Dialect instruction is injected into *every* prompt** (LLMs drift to MSA): Egyptian Arabic
  only, Arabic script only, numbers spelled out as words, no diacritics, TTS-safe punctuation —
  and it names the exact live TTS engine that will speak the line. See `BuildDialectInstruction`.
- Pricing gate: the merchant may not quote until the player has explicitly stated **both** the
  fish species **and** weight; weight alone never identifies the species.

## Price negotiation calculator
**File:** `Merchant/PriceNegotiationCalculator.cs`. Pure deterministic math — no Unity, no
AI. All tuning is named constants at the top.

- **Pricing is fish-data driven** (fractions of the fish's `fairMarketValue`), *not* merchant
  multipliers (those `MerchantConfig` fields are deprecated). The merchant archetype only
  affects the **deception roll** and **patience**.
- `RollDeception(config)` → the single roll that fixes the whole negotiation tone.
- `GenerateOpeningOffer` (honest ~65–80% of fair value, deceptive ~40–60%),
  `GenerateMaxOffer` (honest up to 100%, deceptive ~75–90%, capped per-kg),
  `GenerateMaxRaiseStep` (a single raise covers ~35% of the opening→ceiling gap).
- `EvaluateCounterOffer` — a fully deterministic turn evaluator (Accept/Raise/Hold/Walk). The
  flow controller currently lets the LLM own turns instead, but this remains available to
  gate/override the model. `CalculateSuccessRate(final, fair)` → the 0–100 player score.

The file ends with worked **manual-verification examples** (there's no automated test suite).

## Merchant spawner & NPC
**Files:** `Merchant/MerchantSpawner.cs`, `Merchant/MerchantNPC.cs`.

- `MerchantSpawner` orchestrates **which** merchant approaches and **when** — enforces
  "one active merchant at a time", picks from the scene roster (`Random` round-robin avoiding
  repeats, or `Sequential`), and runs the cooldown between approaches. It does **not** run the
  negotiation; it hands off to the injected `INegotiationService`. A market visit gets one
  auto-approach (reset when the player leaves and re-enters). `PlayerInitiatesWithMerchant` is
  the player-initiated seam (rejected, not queued, if one is already active).
- `MerchantNPC` is the thin per-NPC component carrying its `MerchantConfig` and its
  **per-instance ElevenLabs voice ID** (`AssignedVoiceId`; falls back to the config's voice) —
  each NPC owns its voice for its whole lifetime, there is no shared pool. Voice **must** be a
  verified Egyptian Arabic voice. Exposes `BeginApproach()` / `EndNegotiation(phase)` /
  `OnNegotiationEnded` / `OnApproachStarted` (cosmetic listeners like `MerchantAnimationDriver`
  subscribe to the latter).

`MerchantConfig` (SO) drives one archetype: name, LLM `Personality`, `DeceptionRate`,
`PatienceLevel`, and ElevenLabs voice + per-model voice params (`VoiceSettings`, v2 vs v3 via
`ElevenLabsModelVersion`). `MerchantSystemConfig` (SO) holds the roster + cooldown.

---

# Utilities
**Files:** `Utilities/`.
- **Performance Monitor** — UI-Toolkit overlay (`PerformanceUIControllerUIToolkit` +
  `PerformanceDataProvider`), budgeted by `PerformanceBudgetSettingSO`.
- **Shader/VFX injectors** — `WaterPropertyInjector`, `VertexPainterPropertyInjector`,
  `SetDecalShader`, `SawVFXController`, `PlaneSDFDriver`, `DisableSRPBatching`.
- **Zone handlers** — `ZoneTriggerHandler`, `ZoneRotationHandler`, `ZoneSawingHandler` (trigger
  volumes that drive gameplay). `XRInteractionUtils` has shared XR helpers.

# Editor tools
**Files:** `Assets/Editor/Scripts/` (namespace `DumyatEditor`, wrapped in `#if UNITY_EDITOR`).
Scene Switcher Pro (toolbar), Auto Collider Tool (`AutoColliderWindow` + `ColliderGizmoDrawer`),
`CraftingRecipeRecorder`, `WaterPropertyInjectorEditor`, `VertexPainterPropertyInjectorEditor`,
`MerchantAiSceneSetup`, `VoiceCaptureControllerEditor`. `ValidationTester` and
`CraftingRecipeRecorder` are manual in-Editor dev tools, not automated tests.
