namespace DumyatRuntime
{
    /// <summary>
    /// Identifies the gameplay event that drives a quest's progress.
    /// Each value maps to a specific <see cref="IGameEvent"/> the
    /// <see cref="QuestService"/> subscribes to.
    /// </summary>
    public enum QuestTriggerType
    {
        /// <summary>Progresses when a <see cref="CraftingValidatedEvent"/> fires with a passing result.</summary>
        CraftValidation,

        /// <summary>Progresses when a <see cref="FishCaughtEvent"/> fires.</summary>
        FishCaught,

        /// <summary>Progresses when a <see cref="FishSaleRecordedEvent"/> fires.</summary>
        ItemSold,

        /// <summary>Progresses when a <see cref="BlueprintUnlockedEvent"/> fires.</summary>
        BlueprintUnlocked,

        /// <summary>Progresses when an <see cref="InventoryChangedEvent"/> fires with a positive delta.</summary>
        ItemCollected
    }
}
