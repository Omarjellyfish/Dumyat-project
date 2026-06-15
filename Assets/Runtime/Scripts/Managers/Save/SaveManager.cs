using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace DumyatRuntime
{
    public static class SaveManager
    {
        private const string SAVE_FILE_NAME = "/dumyat_master_save.json";

        // This is the active state of all saved data in memory
        public static GameSaveData CurrentSaveData { get; private set; }

        public static async void SaveGameAsync()
        {
            if (CurrentSaveData == null) return;

            // Serialize on the main thread to ensure Unity safety
            string json = JsonUtility.ToJson(CurrentSaveData, true);
            string savePath = Application.persistentDataPath + SAVE_FILE_NAME;

            // Background file writing
            await Task.Run(() =>
            {
                File.WriteAllText(savePath, json);
            });

            Debug.Log($"SaveManager: Master save written asynchronously to {savePath}");
        }

        public static void LoadGame()
        {
            string savePath = Application.persistentDataPath + SAVE_FILE_NAME;

            if (File.Exists(savePath))
            {
                string json = File.ReadAllText(savePath);
                CurrentSaveData = new GameSaveData();
                JsonUtility.FromJsonOverwrite(json, CurrentSaveData);
                Debug.Log("SaveManager: Master Save Loaded.");
            }
            else
            {
                Debug.Log("SaveManager: No existing save file found. Initializing a fresh GameSaveData.");
                CurrentSaveData = new GameSaveData();
            }
        }
    }
}
