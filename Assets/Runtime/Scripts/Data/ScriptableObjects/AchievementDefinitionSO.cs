using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "NewAchievement", menuName = "Dumyat/Achievements/Achievement Definition")]
    public class AchievementDefinitionSO : ScriptableObject
    {
        [Tooltip("Unique, stable key used in save data. Must never change once shipped.")]
        public string achievementID;

        public string displayName;

        [TextArea(2, 4)]
        public string description;

        [Tooltip("Icon shown in the UI when unlocked.")]
        public Sprite icon;
    }
}
