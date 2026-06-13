using System.IO;
using UnityEngine;

namespace DumyatRuntime
{
    public static class InventorySaveSystem
    {
        private const string SAVE_FILE_NAME = "/inventory_save.json";

        public static void Save(InventorySaveData data)
        {
            string json = JsonUtility.ToJson(data, true);
            string savePath = Application.persistentDataPath + SAVE_FILE_NAME;

            File.WriteAllText(savePath, json);
            Debug.Log($"Inventory Data Saved: {savePath}");
        }

        public static InventorySaveData Load()
        {
            string savePath = Application.persistentDataPath + SAVE_FILE_NAME;

            if (File.Exists(savePath))
            {
                string json = File.ReadAllText(savePath);
                InventorySaveData data = new InventorySaveData();
                JsonUtility.FromJsonOverwrite(json, data);
                Debug.Log("Inventory Data Loaded.");
                return data;
            }
            
            Debug.Log("No existing save file located. Initializing an empty player inventory.");
            return new InventorySaveData();
        }
    }
}
