using System.Collections.Generic;

namespace DumyatRuntime
{
    public interface IAchievementService
    {
        /// <summary>Unlocks the achievement if it isn't already unlocked, saves the game, and fires an event.</summary>
        void UnlockAchievement(string achievementID);

        /// <summary>Returns true if the achievement has been unlocked.</summary>
        bool IsUnlocked(string achievementID);

        /// <summary>Returns all defined achievements in the database.</summary>
        List<AchievementDefinitionSO> GetAllAchievements();
    }
}
