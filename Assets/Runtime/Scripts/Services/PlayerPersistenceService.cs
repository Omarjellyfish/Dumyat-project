using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Plain C# implementation of <see cref="IPlayerPersistenceService"/>, constructed by Reflex.
    /// Replaces the former <c>PlayerPersistence</c> singleton: reads/writes the persistence save
    /// slice through <see cref="ISaveService"/> and publishes <see cref="KeyUnlockedEvent"/>.
    /// </summary>
    public sealed class PlayerPersistenceService : IPlayerPersistenceService
    {
        private readonly ISaveService _saveService;
        private readonly PersistenceSaveData _persistenceData;

        public PlayerPersistenceService(ISaveService saveService)
        {
            _saveService = saveService;
            _persistenceData = _saveService.CurrentSaveData.persistenceData;
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
                _saveService.SaveAsync();

                Debug.Log($"PlayerPersistenceService: Key Unlocked -> '{key}'");
                EventBus.Publish(new KeyUnlockedEvent(key));
            }
        }
    }
}
