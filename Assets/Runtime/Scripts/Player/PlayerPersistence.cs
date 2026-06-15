using System;
using UnityEngine;

namespace DumyatRuntime
{
    public class PlayerPersistence : MonoBehaviour, IManager
    {
        public static PlayerPersistence Singleton { get; private set; }

        public static Action<string> OnKeyUnlocked;

        private PersistenceSaveData _persistenceData;

        private void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }
            Singleton = this;

            if (SaveManager.CurrentSaveData == null)
            {
                SaveManager.LoadGame();
            }

            _persistenceData = SaveManager.CurrentSaveData.persistenceData;
        }

        public bool HasKey(string key)
        {
            if (_persistenceData == null) return false;
            return _persistenceData.persistenceKeys.Contains(key);
        }

        public void AddKey(string key)
        {
            if (_persistenceData == null) return;

            if (!_persistenceData.persistenceKeys.Contains(key))
            {
                _persistenceData.persistenceKeys.Add(key);
                SaveManager.SaveGameAsync();
                
                Debug.Log($"PlayerPersistence: Key Unlocked -> '{key}'");
                OnKeyUnlocked?.Invoke(key);
            }
        }
    }
}
