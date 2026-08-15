using UnityEngine.Scripting;

namespace DumyatRuntime
{
    /// <summary>
    /// Never called. It exists purely so the IL2CPP compiler emits, for every event type a
    /// <see cref="TutorialStepSO"/> is allowed to name, all three generic methods the tutorial reaches
    /// by reflection: <c>IEventService.Subscribe&lt;T&gt;</c>, <c>Unsubscribe&lt;T&gt;</c>, and
    /// <see cref="TutorialRunner.HandleGenericEvent{T}"/>.
    ///
    /// Why it is needed: <see cref="TutorialRunner"/> reaches those through <c>MakeGenericMethod</c>, and
    /// AOT cannot create a new generic instantiation at runtime when the type argument is a value type.
    /// Every event here is a <c>readonly struct</c>. A publisher calling
    /// <c>Publish(new WoodCutEvent(...))</c> generates <c>Publish&lt;WoodCutEvent&gt;</c> and nothing
    /// else — so an event that no C# code ever subscribes to by name has no
    /// <c>Subscribe&lt;WoodCutEvent&gt;</c> in the binary, and the first tutorial step that waits on it
    /// throws <c>ExecutionEngineException</c>. Android builds here are IL2CPP, so this bites on device
    /// while the Editor's JIT hides it completely.
    ///
    /// The handler half used to be missing: only Subscribe/Unsubscribe were preserved, while
    /// <c>HandleGenericEvent&lt;T&gt;</c> — which is equally reflective and equally value-typed — was
    /// not. Routing everything through one <see cref="Preserve{T}"/> call keeps the list of event types
    /// in exactly one place, so that half can never go missing again.
    ///
    /// ADD A LINE HERE whenever a step asset is pointed at a new event type. There is no way to catch a
    /// missing one in the Editor.
    /// </summary>
    internal static class TutorialEventAotHints
    {
        private static void Preserve<T>(IEventService s, TutorialRunner r) where T : IGameEvent
        {
            s.Subscribe<T>(null);
            s.Unsubscribe<T>(null);
            r.HandleGenericEvent<T>(default);
        }

        [Preserve]
        private static void PreserveTutorialEventSubscriptions(IEventService s, TutorialRunner r)
        {
            // Crafting (carpentar_room)
            Preserve<RecipeSelectedEvent>(s, r);
            Preserve<WoodCutEvent>(s, r);
            Preserve<PaintAppliedEvent>(s, r);
            Preserve<GlueAppliedEvent>(s, r);
            Preserve<AttachmentRemovedEvent>(s, r);
            Preserve<CraftingValidatedEvent>(s, r);

            // Fishing (El_manara / Ras El Bar)
            Preserve<FishingRodPickedUpEvent>(s, r);
            Preserve<RodCastArmedEvent>(s, r);
            Preserve<RodCastReleasedEvent>(s, r);
            Preserve<HookLandedInWaterEvent>(s, r);
            Preserve<ReelTurnedEvent>(s, r);
            Preserve<FishCaughtEvent>(s, r);
            Preserve<FishGrabbedEvent>(s, r);
            Preserve<FatherPathCompletedEvent>(s, r);
            Preserve<BoatBoardedEvent>(s, r);
            Preserve<BoatLeftEvent>(s, r);

            // Narrative — the Ras El Bar opening step waits on the father's speech finishing.
            Preserve<DialogueEndedEvent>(s, r);

            // Market (Ezbet Al Bourg)
            Preserve<MerchantSpokeEvent>(s, r);
            Preserve<ScaleContentsChangedEvent>(s, r);
            Preserve<MerchantOfferedPriceEvent>(s, r);
            Preserve<FishSaleRecordedEvent>(s, r);

            // Shipyard
            Preserve<ShipPartSocketedEvent>(s, r);
            Preserve<ShipAssembledEvent>(s, r);

            // Intro / basic controls
            Preserve<PlayerMovedEvent>(s, r);
            Preserve<PlayerGrabbedObjectEvent>(s, r);
            Preserve<PlayerUsedHeldItemEvent>(s, r);
            Preserve<GamePauseToggledEvent>(s, r);
            Preserve<InventoryPanelToggledEvent>(s, r);

            // General-purpose, usable from any sequence
            Preserve<InventoryChangedEvent>(s, r);
            Preserve<QuestCompletedEvent>(s, r);
            Preserve<KeyUnlockedEvent>(s, r);
        }
    }
}
