using System.Collections.Generic;

namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for the player inventory. Consumers inject this instead of reaching
    /// for <c>InventoryManager.Singleton</c>. Backed by <see cref="InventoryService"/>.
    /// </summary>
    public interface IInventoryService
    {
        /// <summary>Read-only view of the items currently held.</summary>
        IReadOnlyList<InventoryItem> CurrentItems { get; }

        /// <summary>The item database used to resolve item IDs to <see cref="ItemData"/>.</summary>
        ItemDatabase ItemDatabase { get; }

        /// <summary>Adds <paramref name="amount"/> of item <paramref name="idToAdd"/> and persists.</summary>
        void AddItemToInventory(string idToAdd, int amount);

        /// <summary>Removes items and publishes an <see cref="ItemDroppedEvent"/> so the world spawns them.</summary>
        void DropItem(string idToDrop, int amountToDrop = 1);
    }
}
