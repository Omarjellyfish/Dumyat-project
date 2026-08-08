namespace DumyatRuntime
{
    /// <summary>Published by <see cref="InventoryService"/> when an item's quantity is added, removed, or changed.</summary>
    /// <summary>
    /// The player opened or closed their bag. Distinct from <see cref="InventoryChangedEvent"/>, which
    /// is about what is IN the bag — this is about the act of looking, which is the thing the intro
    /// tutorial teaches and the only way to tell the player found the control.
    ///
    /// Published by whichever inventory view is in the scene; both the uGUI
    /// (<see cref="InventoryWorldUI"/>) and UI Toolkit (<see cref="InventoryToolkitUI"/>) panels raise
    /// it, so a listener does not need to know which one a scene uses.
    /// </summary>
    public readonly struct InventoryPanelToggledEvent : IGameEvent
    {
        public readonly bool IsOpen;

        public InventoryPanelToggledEvent(bool isOpen)
        {
            IsOpen = isOpen;
        }
    }

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
