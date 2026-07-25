using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "AchievementDatabase", menuName = "Dumyat/Achievements/Achievement Database")]
    public class AchievementDatabaseSO : ScriptableObject
    {
        [SerializeField] private List<AchievementDefinitionSO> _achievements = new List<AchievementDefinitionSO>();

        public IReadOnlyList<AchievementDefinitionSO> AllAchievements => _achievements;

        public AchievementDefinitionSO GetByID(string id)
        {
            for (int i = 0; i < _achievements.Count; i++)
            {
                if (_achievements[i] != null && _achievements[i].achievementID == id)
                {
                    return _achievements[i];
                }
            }
            return null;
        }
    }
}
