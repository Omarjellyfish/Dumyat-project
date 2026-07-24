---
name: dumyat-workflow
description: The specific architectural and structural rules for the Dumyat project, including Reflex DI usage and folder hierarchy.
---

## Dumyat Project Architecture & Structure

This workflow is made for the Dumyat project, defining how its hierarchy is set, what questions should be asked before proceeding, and the architecture of the project.

### Core Architecture Rules

1. **Dependency Injection (Reflex DI)**:
   - Always adhere to the Reflex Dependency Injection model.
   - Use `[Inject]` for resolving services (e.g., `IEventService`, `ISceneLoadService`) in MonoBehaviours.
   - Do NOT use singletons (`Instance`) or `FindObjectOfType` for core services.
   - Inject dependencies via the `[Inject]` attribute or via constructors for non-MonoBehaviours.
   - Scene-specific dependencies belong in the `SceneInstaller`, while global persisting dependencies belong in the `GameInstaller`.
   
2. **Folder Structure & Hierarchy**:
   - Maintain the established project folder structure in `Assets/Runtime/Scripts/`:
     - `Services/` for core injected systems (e.g., `SceneLoadService`, `CraftingService`).
     - `UI/` for UI Toolkit and Canvas scripts.
     - `Systems/` for gameplay systems and interaction logic (e.g., `CarTransitionHandler`, `BusStation`).
     - `Data/ScriptableObjects/` for data definitions (e.g., `TravelDestination`).
     - `Interfaces/` for all abstractions (e.g., `IEventService`).
     - `Events/` for event structs.
   - Place new scripts in the exact correct matching folder based on their role. Do not create new top-level folders without asking.
