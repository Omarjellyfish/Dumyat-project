using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Plain C# implementation of <see cref="IInventoryService"/>, constructed by Reflex.
    /// Replaces the former <c>InventoryManager</c> MonoBehaviour-singleton: it holds no scene state,
    /// so its dependencies (the shared save store and the item database asset) are injected via the
    /// constructor by <see cref="GameInstaller"/>. Cross-system reactions flow through the injected
    /// <see cref="IEventService"/> (<see cref="InventoryChangedEvent"/> / <see cref="ItemDroppedEvent"/>).
    /// </summary>
    public sealed class InventoryService : IInventoryService
    {
        private readonly ISaveService _saveService;
        private readonly ItemDatabase _itemDatabase;
        private readonly IEventService _eventService;
        private readonly InventorySaveData _currentInventory;

        public InventoryService(ISaveService saveService, ItemDatabase itemDatabase, IEventService eventService)
        {
            _saveService = saveService;
            _itemDatabase = itemDatabase;
            _eventService = eventService;
            _currentInventory = _saveService.CurrentSaveData.inventoryData;
        }

        public IReadOnlyList<InventoryItem> CurrentItems => _currentInventory.items;
        public ItemDatabase ItemDatabase => _itemDatabase;

        public void AddItemToInventory(string idToAdd, int amount)
        {
            if (_itemDatabase == null)
            {
                Debug.LogError("InventoryService: Database reference is unassigned!");
                return;
            }

            ItemData blueprint = _itemDatabase.GetItemByID(idToAdd);
            if (blueprint == null) return;

            InventoryItem existingItem = _currentInventory.items.Find(item => item.ItemID == idToAdd);

            if (existingItem != null)
            {
                existingItem.Quantity += amount;
                _eventService.Publish(new InventoryChangedEvent(existingItem));
            }
            else
            {
                InventoryItem newItem = new InventoryItem(idToAdd, amount);
                _currentInventory.items.Add(newItem);
                _eventService.Publish(new InventoryChangedEvent(newItem));
            }

            Debug.Log($"Inventory Data: Appended {amount}x '{blueprint.DisplayName}'.");
            _saveService.SaveAsync();
        }

        /// <summary>
        /// Deducts item data, updates structural lists, and broadcasts an event to physically spawn the asset.
        /// </summary>
        public void DropItem(string idToDrop, int amountToDrop = 1)
        {
            InventoryItem item = _currentInventory.items.Find(i => i.ItemID == idToDrop);

            if (item == null || item.Quantity < amountToDrop)
            {
                Debug.LogWarning($"Inventory Data: Unable to drop {amountToDrop}x '{idToDrop}'. Insufficient quantity.");
                return;
            }

            item.Quantity -= amountToDrop;

            if (item.Quantity <= 0)
            {
                _currentInventory.items.Remove(item);
            }

            ItemData blueprint = _itemDatabase.GetItemByID(idToDrop);
            if (blueprint != null)
            {
                _eventService.Publish(new ItemDroppedEvent(blueprint, amountToDrop));
            }

            _eventService.Publish(new InventoryChangedEvent(item));
            _saveService.SaveAsync();
        }
    }
}
