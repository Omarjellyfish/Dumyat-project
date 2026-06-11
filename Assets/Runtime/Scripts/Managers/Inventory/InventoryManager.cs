using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

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
        public ItemDatabase itemDatabase; //contains item data which has the 2dsprite, the prefab, and more

        [Header("Runtime State")]
        public InventorySaveData currentInventory = new InventorySaveData();

        [Header("Global Hooks")]
        public static InventoryManager Singleton { get; private set; }

        // Broadcasts the item state whenever modifying quantities 
        public static Action<InventoryItem> OnInventoryChanged;


        public static Action<string, int> OnItemDropped;

        private const string SAVE_FILE_NAME = "/inventory_save.json";

        private void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }
            Singleton = this;
            DontDestroyOnLoad(gameObject);

            LoadInventory();
        }

        public void AddItemToInventory(string idToAdd, int amount)
        {
            if (itemDatabase == null)
            {
                Debug.LogError("InventoryManager: Database reference is unassigned!");
                return;
            }

            ItemData blueprint = itemDatabase.GetItemByID(idToAdd);
            if (blueprint == null) return;

            InventoryItem existingItem = currentInventory.items.Find(item => item.itemID == idToAdd);

            if (existingItem != null)
            {
                existingItem.quantity += amount;
                OnInventoryChanged?.Invoke(existingItem);
            }
            else
            {
                InventoryItem newItem = new InventoryItem(idToAdd, amount);
                currentInventory.items.Add(newItem);
                OnInventoryChanged?.Invoke(newItem);
            }

            Debug.Log($"Inventory Data: Appended {amount}x '{blueprint.displayName}'.");
            SaveInventory();
        }

        /// <summary>
        /// Deducts item data, updates structural lists, and broadcasts an event to physically spawn the asset.
        /// </summary>
        public void DropItem(string idToDrop, int amountToDrop = 1)
        {
            InventoryItem item = currentInventory.items.Find(i => i.itemID == idToDrop);

            if (item == null || item.quantity < amountToDrop)
            {
                Debug.LogWarning($"Inventory Data: Unable to drop {amountToDrop}x '{idToDrop}'. Insufficient quantity.");
                return;
            }

            item.quantity -= amountToDrop;

            if (item.quantity <= 0)
            {
                currentInventory.items.Remove(item);
            }

            OnItemDropped?.Invoke(idToDrop, amountToDrop);

            OnInventoryChanged?.Invoke(item);
            SaveInventory();
        }

        #region Persistence Layer (JSON File Serialization)

        public void SaveInventory()
        {
            string json = JsonUtility.ToJson(currentInventory, true);
            string savePath = Application.persistentDataPath + SAVE_FILE_NAME;

            File.WriteAllText(savePath, json);
            Debug.Log($"Inventory Data Saved: {savePath}");
        }

        public void LoadInventory()
        {
            string savePath = Application.persistentDataPath + SAVE_FILE_NAME;

            if (File.Exists(savePath))
            {
                string json = File.ReadAllText(savePath);
                JsonUtility.FromJsonOverwrite(json, currentInventory);
                Debug.Log("Inventory Data Loaded.");
            }
            else
            {
                Debug.Log("No existing save file located. Initializing an empty player inventory.");
            }
        }

        #endregion
    }
}