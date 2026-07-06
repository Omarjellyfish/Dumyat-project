namespace DumyatRuntime
{
    /// <summary>Published by <see cref="InventoryService"/> when an item's quantity is added, removed, or changed.</summary>
    public readonly struct InventoryChangedEvent : IGameEvent
    {
        public readonly InventoryItem Item;

        public InventoryChangedEvent(InventoryItem item)
        {
            Item = item;
        }
    }

    /// <summary>Published by <see cref="InventoryService"/> when the player drops an item into the world.</summary>
    public readonly struct ItemDroppedEvent : IGameEvent
    {
        public readonly ItemData Item;
        public readonly int Amount;

        public ItemDroppedEvent(ItemData item, int amount)
        {
            Item = item;
            Amount = amount;
        }
    }
}
