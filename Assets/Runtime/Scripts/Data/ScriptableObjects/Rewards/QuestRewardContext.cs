namespace DumyatRuntime
{
    /// <summary>
    /// Passed to <see cref="QuestRewardSO.GrantReward"/> so reward implementations
    /// can interact with game services without needing DI themselves.
    /// </summary>
    public struct QuestRewardContext
    {
        public IEventService EventService;
        public ICraftingService CraftingService;
        public IInventoryService InventoryService;
    }
}
