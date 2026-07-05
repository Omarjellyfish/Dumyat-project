using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Reflex-injected wrapper over the master save store, exposing <see cref="ISaveService"/>.
    /// </summary>
    public sealed class SaveService : ISaveService
    {
        private const string SAVE_FILE_NAME = "/dumyat_master_save.json";

        private GameSaveData _currentSaveData;

        public GameSaveData CurrentSaveData
        {
            get
            {
                if (_currentSaveData == null)
                {
                    Load();
                }
                return _currentSaveData;
            }
        }

        public void Load()
        {
            string savePath = Application.persistentDataPath + SAVE_FILE_NAME;

            if (File.Exists(savePath))
            {
                string json = File.ReadAllText(savePath);
                _currentSaveData = new GameSaveData();
                JsonUtility.FromJsonOverwrite(json, _currentSaveData);
                Debug.Log("SaveService: Master Save Loaded.");
            }
            else
            {
                Debug.Log("SaveService: No existing save file found. Initializing a fresh GameSaveData.");
                _currentSaveData = new GameSaveData();
            }
        }

        public async void SaveAsync()
        {
            if (_currentSaveData == null) return;

            string json = JsonUtility.ToJson(_currentSaveData, true);
            string savePath = Application.persistentDataPath + SAVE_FILE_NAME;

            await Task.Run(() =>
            {
                File.WriteAllText(savePath, json);
            });

            Debug.Log($"SaveService: Master save written asynchronously to {savePath}");
        }
    }
}
