using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace DumyatRuntime
{
    [Serializable]
    public class InventorySaveData
    {
        public List<InventoryItem> items = new List<InventoryItem>();
    }

    public class InventoryManager : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Contains all items and their individaul item data,  Assign your central database asset here.")]
        [SerializeField] private ItemDatabase _itemDatabase; 

        [Header("Runtime State")]
        [SerializeField] private InventorySaveData _currentInventory = new InventorySaveData();

        [Header("Global Hooks")]
        public static InventoryManager Singleton { get; private set; }

        // Broadcasts the item state whenever modifying quantities 
        public static Action<InventoryItem> OnInventoryChanged;

        // Broadcasts the item data and amount dropped
        public static Action<ItemData, int> OnItemDropped;

        private const string SAVE_FILE_NAME = "/inventory_save.json";

        public IReadOnlyList<InventoryItem> CurrentItems => _currentInventory.items;

        private void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }
            Singleton = this;
            DontDestroyOnLoad(gameObject);

            _currentInventory = InventorySaveSystem.Load();
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
                OnInventoryChanged?.Invoke(existingItem);
            }
            else
            {
                InventoryItem newItem = new InventoryItem(idToAdd, amount);
                _currentInventory.items.Add(newItem);
                OnInventoryChanged?.Invoke(newItem);
            }

            Debug.Log($"Inventory Data: Appended {amount}x '{blueprint.DisplayName}'.");
            InventorySaveSystem.Save(_currentInventory);
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
                OnItemDropped?.Invoke(blueprint, amountToDrop);
            }

            OnInventoryChanged?.Invoke(item);
            InventorySaveSystem.Save(_currentInventory);
        }
    }
}