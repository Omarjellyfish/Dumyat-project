namespace DumyatRuntime
{
    public struct AchievementUnlockedEvent : IGameEvent
    {
        public readonly string AchievementID;

        public AchievementUnlockedEvent(string achievementID)
        {
            AchievementID = achievementID;
        }
    }
}
