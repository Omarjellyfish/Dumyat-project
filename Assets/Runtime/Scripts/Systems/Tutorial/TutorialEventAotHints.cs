using UnityEngine.Scripting;

namespace DumyatRuntime
{
    /// <summary>
    /// Never called. It exists purely so the IL2CPP compiler emits
    /// <c>IEventService.Subscribe&lt;T&gt;</c> / <c>Unsubscribe&lt;T&gt;</c> for every event type a
    /// <see cref="TutorialStepSO"/> is allowed to name.
    ///
    /// Why it is needed: <see cref="TutorialRunner"/> reaches those methods through
    /// <c>MakeGenericMethod</c>, and AOT cannot create a new generic instantiation at runtime when the
    /// type argument is a value type. Every event here is a <c>readonly struct</c>. A publisher calling
    /// <c>Publish(new WoodCutEvent(...))</c> generates <c>Publish&lt;WoodCutEvent&gt;</c> and nothing
    /// else — so an event that no C# code ever subscribes to by name has no
    /// <c>Subscribe&lt;WoodCutEvent&gt;</c> in the binary, and the first tutorial step that waits on it
    /// throws <c>ExecutionEngineException</c>. Android builds here are IL2CPP with High managed
    /// stripping, so this bites on device while the Editor's JIT hides it completely.
    ///
    /// ADD A LINE HERE whenever a step asset is pointed at a new event type. There is no way to catch
    /// a missing one in the Editor.
    /// </summary>
    internal static class TutorialEventAotHints
    {
        [Preserve]
        private static void PreserveTutorialEventSubscriptions(IEventService s)
        {
            // Crafting (carpentar_room)
            s.Subscribe<RecipeSelectedEvent>(null);        s.Unsubscribe<RecipeSelectedEvent>(null);
            s.Subscribe<WoodCutEvent>(null);               s.Unsubscribe<WoodCutEvent>(null);
            s.Subscribe<PaintAppliedEvent>(null);          s.Unsubscribe<PaintAppliedEvent>(null);
            s.Subscribe<GlueAppliedEvent>(null);           s.Unsubscribe<GlueAppliedEvent>(null);
            s.Subscribe<AttachmentRemovedEvent>(null);     s.Unsubscribe<AttachmentRemovedEvent>(null);
            s.Subscribe<CraftingValidatedEvent>(null);     s.Unsubscribe<CraftingValidatedEvent>(null);

            // Fishing (El_manara / Ras El Bar)
            s.Subscribe<FishingRodPickedUpEvent>(null);    s.Unsubscribe<FishingRodPickedUpEvent>(null);
            s.Subscribe<RodCastArmedEvent>(null);          s.Unsubscribe<RodCastArmedEvent>(null);
            s.Subscribe<RodCastReleasedEvent>(null);       s.Unsubscribe<RodCastReleasedEvent>(null);
            s.Subscribe<HookLandedInWaterEvent>(null);     s.Unsubscribe<HookLandedInWaterEvent>(null);
            s.Subscribe<ReelTurnedEvent>(null);            s.Unsubscribe<ReelTurnedEvent>(null);
            s.Subscribe<FishCaughtEvent>(null);            s.Unsubscribe<FishCaughtEvent>(null);
            s.Subscribe<FishGrabbedEvent>(null);           s.Unsubscribe<FishGrabbedEvent>(null);

            // Market (Ezbet Al Bourg)
            s.Subscribe<MerchantSpokeEvent>(null);         s.Unsubscribe<MerchantSpokeEvent>(null);
            s.Subscribe<ScaleContentsChangedEvent>(null);  s.Unsubscribe<ScaleContentsChangedEvent>(null);
            s.Subscribe<MerchantOfferedPriceEvent>(null);  s.Unsubscribe<MerchantOfferedPriceEvent>(null);
            s.Subscribe<FishSaleRecordedEvent>(null);      s.Unsubscribe<FishSaleRecordedEvent>(null);

            // Shipyard
            s.Subscribe<ShipPartSocketedEvent>(null);      s.Unsubscribe<ShipPartSocketedEvent>(null);
            s.Subscribe<ShipAssembledEvent>(null);         s.Unsubscribe<ShipAssembledEvent>(null);

            // Intro / basic controls
            s.Subscribe<PlayerMovedEvent>(null);           s.Unsubscribe<PlayerMovedEvent>(null);
            s.Subscribe<PlayerGrabbedObjectEvent>(null);   s.Unsubscribe<PlayerGrabbedObjectEvent>(null);
            s.Subscribe<PlayerUsedHeldItemEvent>(null);    s.Unsubscribe<PlayerUsedHeldItemEvent>(null);
            s.Subscribe<GamePauseToggledEvent>(null);      s.Unsubscribe<GamePauseToggledEvent>(null);
            s.Subscribe<InventoryPanelToggledEvent>(null); s.Unsubscribe<InventoryPanelToggledEvent>(null);

            // General-purpose, usable from any sequence
            s.Subscribe<InventoryChangedEvent>(null);      s.Unsubscribe<InventoryChangedEvent>(null);
            s.Subscribe<QuestCompletedEvent>(null);        s.Unsubscribe<QuestCompletedEvent>(null);
            s.Subscribe<KeyUnlockedEvent>(null);           s.Unsubscribe<KeyUnlockedEvent>(null);
        }
    }
}
