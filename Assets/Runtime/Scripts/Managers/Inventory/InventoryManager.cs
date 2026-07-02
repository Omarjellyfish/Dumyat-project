using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace DumyatRuntime
{
    public class InventoryManager : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Contains all items and their individaul item data,  Assign your central database asset here.")]
        [SerializeField] private ItemDatabase _itemDatabase;

        [Header("Runtime State")]
        private InventorySaveData _currentInventory;

        [Header("Global Hooks")]
        public static InventoryManager Singleton { get; private set; }

        public IReadOnlyList<InventoryItem> CurrentItems => _currentInventory.items;
        public ItemDatabase ItemDatabase => _itemDatabase;

        private void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }
            Singleton = this;
            DontDestroyOnLoad(gameObject);

            if (SaveManager.CurrentSaveData == null)
            {
                SaveManager.LoadGame();
            }

            _currentInventory = SaveManager.CurrentSaveData.inventoryData;
        }

        public void AddItemToInventory(string idToAdd, int amount)
        {
            if (_itemDatabase == null)
            {
                Debug.LogError("InventoryManager: Database reference is unassigned!");
                return;
            }

            ItemData blueprint = _itemDatabase.GetItemByID(idToAdd);
            if (blueprint == null) return;

            InventoryItem existingItem = _currentInventory.items.Find(item => item.ItemID == idToAdd);

            if (existingItem != null)
            {
                existingItem.Quantity += amount;
                EventBus.Publish(new InventoryChangedEvent(existingItem));
            }
            else
            {
                InventoryItem newItem = new InventoryItem(idToAdd, amount);
                _currentInventory.items.Add(newItem);
                EventBus.Publish(new InventoryChangedEvent(newItem));
            }

            Debug.Log($"Inventory Data: Appended {amount}x '{blueprint.DisplayName}'.");
            SaveManager.SaveGameAsync();
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
                EventBus.Publish(new ItemDroppedEvent(blueprint, amountToDrop));
            }

            EventBus.Publish(new InventoryChangedEvent(item));
            SaveManager.SaveGameAsync();
        }
    }
}