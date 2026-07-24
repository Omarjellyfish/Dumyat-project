using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    public sealed class AchievementService : IAchievementService
    {
        private readonly ISaveService _saveService;
        private readonly IEventService _eventService;
        private readonly AchievementDatabaseSO _database;
        private readonly AchievementSaveData _saveData;

        public AchievementService(ISaveService saveService, IEventService eventService, AchievementDatabaseSO database)
        {
            _saveService = saveService;
            _eventService = eventService;
            _database = database;
            _saveData = _saveService.CurrentSaveData.achievementData;

            Debug.Log($"[AchievementService] Initialised — {_saveData.unlockedAchievementIDs.Count} achievements unlocked.");
        }

        public void UnlockAchievement(string achievementID)
        {
            if (string.IsNullOrEmpty(achievementID)) return;

            if (_saveData.unlockedAchievementIDs.Contains(achievementID))
            {
                // Already unlocked, ignore
                return;
            }

            var definition = _database.GetByID(achievementID);
            if (definition == null)
            {
                Debug.LogWarning($"[AchievementService] Tried to unlock unknown achievement: '{achievementID}'");
                return;
            }

            _saveData.unlockedAchievementIDs.Add(achievementID);
            _saveService.SaveAsync();
            
            _eventService.Publish(new AchievementUnlockedEvent(achievementID));
            Debug.Log($"[AchievementService] Achievement Unlocked: {definition.displayName} ({achievementID})");
        }

        public bool IsUnlocked(string achievementID)
        {
            return _saveData.unlockedAchievementIDs.Contains(achievementID);
        }

        public List<AchievementDefinitionSO> GetAllAchievements()
        {
            var list = new List<AchievementDefinitionSO>();
            if (_database != null)
            {
                list.AddRange(_database.AllAchievements);
            }
            return list;
        }
    }
}
