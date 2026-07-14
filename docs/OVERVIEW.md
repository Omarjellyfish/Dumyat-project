# Dumyat — Project Overview

The starting point for understanding this project. Read this first, then dive into
[ARCHITECTURE.md](ARCHITECTURE.md) (how it's built and how to add to it),
[SYSTEMS.md](SYSTEMS.md) (how each system works and how to use it), and
[MERCHANT_CONTEXT.md](MERCHANT_CONTEXT.md) (locale + tone rules for the AI merchant).

---

## What Dumyat is

**Dumyat** is a **VR game about Egyptian traditional life** set in Dumyat (Damietta), a
coastal city known for fishing, carpentry, and ship-building. The player is a child of a
traditional Dumyatti family, learning local trades and daily-life customs alongside their
father.

The game is built around grounded "daily life" activities rather than a single mechanic.
Two experiences carry it today:

1. **VR carpentry / crafting** — the player physically **cuts, assembles, glues, nails, and
   paints wood** at crafting stations to build objects from blueprints. Tools behave like
   real tools (constrained motion, haptics, collision audio), and cut pieces become new
   grabbable, craftable, paintable objects.

2. **AI fish-market haggling** — at the **El Borg fish auction**, the player sells fish
   they caught earlier by **negotiating out loud, in Egyptian Arabic**, with NPC merchants.
   Speech is transcribed, an LLM plays the merchant in character, and the reply is spoken
   back with an Egyptian Arabic voice — while deterministic game logic keeps the economics
   honest.

Both live in the same world, share the same architecture, and are wired together through
dependency injection and an event bus.

---

## Design pillars

- **Grounded and culturally authentic.** Dialogue is **Egyptian Arabic (colloquial)**, not
  Modern Standard Arabic — getting this right is a hard constraint, not a nicety (see
  [MERCHANT_CONTEXT.md](MERCHANT_CONTEXT.md)). Merchants are warm, theatrical, shrewd
  vendors who haggle and occasionally fudge a weight — never cartoon villains.
- **Physical, tool-based VR.** Interaction is embodied: real saw strokes, real brush
  strokes, controller haptics, spatial audio.
- **Small, composable components.** Behaviour is assembled from single-responsibility
  MonoBehaviours designers can mix onto GameObjects — not god-classes.
- **Decoupled by construction.** Systems depend on capability *interfaces* (DI) and react
  through *events*, never hard references or singletons — so features drop in without
  editing existing code.
- **AI on rails.** The language model colours dialogue and picks a per-turn action, but
  never decides game-affecting numbers; deterministic C# owns pricing, deception, and
  outcomes, and validates everything the model returns.

---

## Technology

| Area | Choice |
|---|---|
| Engine | **Unity 6 (6000.3.7f1)**, URP |
| VR | **OpenXR** + XR Interaction Toolkit 3.3.1 + XR Hands; XR Device Simulator for headset-free testing |
| DI | **Reflex** (`com.gustavopsantos.reflex` 14.3.1) |
| Mesh slicing | **EzySlice** (vendored) |
| LLM | **Gemini** / **Groq** (pooled, priority + fallback) |
| Speech | **ElevenLabs** TTS + Scribe STT (pooled) |
| Negotiation authoring | optional **Unity Behavior** graph, with a coded fallback flow |
| Save | single JSON blob via `JsonUtility` |

There is **no CI and no automated tests** — build and run through the Unity Editor. The
installed Test Framework has no project test assemblies; `ValidationTester` and
`CraftingRecipeRecorder` are manual in-Editor dev tools.

---

## How it fits together (one paragraph)

At launch a **root Reflex container** (`GameInstaller` on the ProjectScope prefab) provides
app-wide services — save, inventory, crafting unlocks, game state, the event hub, audio,
haptics. Each gameplay scene adds a **scene container** (`SceneInstaller` subclass) for
services that need the live rig or scene objects. Components receive what they need via
`[Inject]` against **interfaces**, and announce things that happen by publishing small
`readonly struct` events on **`IEventService`**; other systems subscribe. The AI merchant
pipeline layers a **provider pool** on top: LLM/TTS/STT *managers* are the injected services,
and concrete vendor *clients* are auto-discovered into their manager's pool by priority, so a
provider can be added or swapped without touching consumers. Full detail in
[ARCHITECTURE.md](ARCHITECTURE.md).

---

## Repository layout (top level)

```
Assets/
├── Runtime/            Project.Runtime (namespace DumyatRuntime) — ALL gameplay + AI code
│   ├── Scripts/        DI, Interfaces, Services, Events, Managers, Data, Systems,
│   │                   AI, Merchant, UI, Player, Utilities   (see ARCHITECTURE.md §9)
│   ├── Art/            environments, scenes, materials, VFX
│   ├── Data Layer/     authored ScriptableObject assets (merchants, etc.)
│   ├── Prefabs/        including DI/ scope prefabs
│   └── Packages/       vendored EzySlice
├── Editor/             Project.Editor (namespace DumyatEditor) — tools, Editor-only
├── External/           Project.External (namespace DumyatExternal) — reserved, empty
├── Scenes/             including "AI Test Scene" and demo scenes
└── Samples/            third-party samples — DO NOT EDIT
docs/                   OVERVIEW / ARCHITECTURE / SYSTEMS / MERCHANT_CONTEXT
Library/                generated / third-party package cache — DO NOT EDIT
```

---

## Running the project

- Open with **Unity Hub / Editor 6000.3.7f1**. Build/run through the Editor (solution
  `Dumyat-project.slnx`).
- **Play** a scene in the Editor; use the **XR Device Simulator** to play-test without a
  headset.
- **Switch scenes** via the **Scene Switcher Pro** toolbar dropdown (toggle "All Scenes" to
  list every `.unity`).
- Gameplay scenes live under `Assets/Runtime/Art/Scenes/`; the merchant pipeline runs in the
  **AI Test Scene** (`Assets/Scenes/`).

> **Gotcha:** the only build-enabled scene (`carpentar_room.unity`) is currently missing on
> this branch — fix the Build Settings scene list before making a player build. See
> [CLAUDE.md](../CLAUDE.md) ▸ Gotchas.

---

## Current status & notable seams

- **Active branch:** `AI-Integration` — the fish-market merchant negotiation is the focus of
  recent work and is the newest, most complex subsystem.
- **Working, in-Editor exercisable:** the DI/event/save core, VR carpentry systems, and the
  full voice→LLM→voice merchant loop (with defensive recovery at every external call).
- **Known stubs / integration points** (search for `STUB` / `TODO`):
  - `NegotiationFlowController` has stub seams for **subtitle UI**, **thinking cues**, and a
    **scripted-dialogue fallback** (currently ends the negotiation cleanly).
  - `MerchantSpawner.PlayerHasFish()` stands in for the real inventory query;
    fish-to-sell resolution falls back to an Inspector debug fish when no `IFishInventory`
    is present.
  - STT `LanguageCode` and each merchant's voice ID must be **verified for Egyptian dialect**
    before shipping — do not assume a generic "Arabic" tag suffices.
- **Doc drift to be aware of:** [CLAUDE.md](../CLAUDE.md) still describes cross-system events
  as a static `EventBus`; on this branch that is an **injected `IEventService`**. The `docs/`
  files reflect the code as it actually is.

---

## Where to go next

| You want to… | Read |
|---|---|
| Understand how the pieces connect and how to add a system | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Use or extend a specific system (cutting, crafting, merchant, audio…) | [SYSTEMS.md](SYSTEMS.md) |
| Write any merchant dialogue / voice / STT code | [MERCHANT_CONTEXT.md](MERCHANT_CONTEXT.md) |
| Engine, build, scenes, conventions, gotchas | [CLAUDE.md](../CLAUDE.md) |
