# Negotiation Behavior Graph — authoring & scene setup

The merchant negotiation can be driven by a **Unity Behavior** graph instead of the legacy coded
loop in [`NegotiationFlowController`](../NegotiationFlowController.cs). The C# is done; the graph
asset and its wiring are authored in the **Behavior visual editor** (that's the point of the
package). This file is the build recipe.

## How it fits together

- `MerchantSpawner` still calls `NegotiationFlowController.BeginNegotiation(merchant)`.
- If a `BehaviorGraphAgent` **with a graph** is assigned to the controller's `_agent` field, it
  `Restart()`s the graph. Otherwise the **legacy coded flow runs** (nothing breaks before the graph
  exists — this is the regression safety net).
- The graph's nodes call the `Bt_*` step methods on `NegotiationFlowController.Singleton`. The
  controller owns the state (pricing, LLM, TTS, STT via `VoiceCaptureController`); the graph owns the
  **sequencing, the turn loop, and the branches**.

## Custom nodes (already compiled — appear in the editor's Add-node menu)

Conditions (`Conditions/Merchant`): **Voice Mode Available**, **Negotiation Ongoing**.
Actions (`Action/Merchant`): **Setup Negotiation**, **Generate Opening Line**, **Speak Merchant
Line**, **Listen For Player**, **Process Player Turn**, **Apply Turn Result**, **Recover Turn**,
**Resolve Negotiation**, **Scripted Fallback**.

## Graph shape to build

```
Selector (root)
├─ Sequence  "AI negotiation"
│   ├─ [Condition] Voice Mode Available
│   ├─ Setup Negotiation
│   ├─ Generate Opening Line
│   ├─ Speak Merchant Line              (opening line)
│   ├─ Repeat While: Negotiation Ongoing
│   │    └─ Sequence  "turn"
│   │         ├─ Selector  "get a turn or recover"
│   │         │    ├─ Sequence
│   │         │    │    ├─ Listen For Player
│   │         │    │    ├─ Process Player Turn
│   │         │    │    └─ Apply Turn Result
│   │         │    └─ Recover Turn        (runs if the turn above failed)
│   │         └─ Speak Merchant Line      (speaks the real line OR the "say again" line)
│   └─ Resolve Negotiation
└─ Scripted Fallback                      (runs if the AI sequence failed early:
                                           no consent / no fish / opening failed)
```

Why this shape:
- **Voice Mode Available** false, **Setup** false (no fish), or **Generate Opening Line** failure →
  the AI Sequence fails → the root Selector runs **Scripted Fallback**.
- In the loop, if **Listen** or **Process Player Turn** fails, the inner Selector runs **Recover
  Turn**, which queues a "say that again" line (spoken by the following **Speak Merchant Line**) — or,
  after `MaxConsecutiveRecoveries` / on connectivity loss, marks the negotiation terminal so the loop
  exits into **Resolve Negotiation**.
- **Apply Turn Result** sets the negotiation terminal on Accept/WalkAway; the closing line is spoken,
  then **Negotiation Ongoing** is false and the loop exits to **Resolve Negotiation**.

Blackboard variables are **not required** — the nodes read state from
`NegotiationFlowController.Singleton`.

## Build steps

1. Open Unity so `com.unity.behavior` imports and the custom nodes compile.
2. `Assets ▸ Create ▸ Behavior ▸ Behavior Graph`; name it e.g. `NegotiationBehavior` (suggested
   location `Assets/Runtime/Data Layer/Merchants/`).
3. Recreate the tree above by adding the custom nodes from the Add-node menu (categories
   `Action/Merchant` and `Conditions/Merchant`). Use the built-in **Sequence**, **Selector**, and
   **Repeat While Condition** flow nodes for the composites.
4. On the `Negotiation` GameObject in the AI Test Scene: `Add Component ▸ Behavior Graph Agent`,
   assign the `NegotiationBehavior` graph, and drag that agent into
   `NegotiationFlowController ▸ Behavior Graph ▸ Agent`.
5. Merchant-test wiring (from the previous task): disable the `STT Voice Harness Test` object, enable
   `== TEST DRIVER ==`, keep the random fish provider (auto-added by the test driver).

## Verify

- Enter Play: the Behavior editor's live graph view highlights the running node each phase; the
  Console shows the `[Negotiation]` sequence (opening offer → line → listen → transcript → turn →
  loop → Accept/WalkAway → resolved), and `stt_debug_log.txt` still logs the Arabic conversation.
- Remove the graph (or the agent) to confirm the **legacy coded flow** still runs a full negotiation.

## Notes / caveats

- Node attribute/override signatures target Unity Behavior 1.0.16. If the editor's generated node
  template differs on your version, reconcile these classes against a freshly generated node (the
  `Bt_*` calls stay the same).
- `Recover Turn` / `Scripted Fallback` both end via the controller's idempotent teardown, so the
  spawner's one-at-a-time lock + cooldown release correctly.
