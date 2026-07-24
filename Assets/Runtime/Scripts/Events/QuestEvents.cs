namespace DumyatRuntime
{
    /// <summary>Published by <see cref="QuestService"/> when a quest becomes active.</summary>
    public readonly struct QuestActivatedEvent : IGameEvent
    {
        public readonly string QuestID;

        public QuestActivatedEvent(string questID)
        {
            QuestID = questID;
        }
    }

    /// <summary>Published by <see cref="QuestService"/> when an active quest's progress counter advances.</summary>
    public readonly struct QuestProgressedEvent : IGameEvent
    {
        public readonly string QuestID;
        public readonly int CurrentProgress;
        public readonly int TargetCount;

        public QuestProgressedEvent(string questID, int currentProgress, int targetCount)
        {
            QuestID = questID;
            CurrentProgress = currentProgress;
            TargetCount = targetCount;
        }
    }

    /// <summary>Published by <see cref="QuestService"/> when a quest reaches its target and is marked complete.</summary>
    public readonly struct QuestCompletedEvent : IGameEvent
    {
        public readonly string QuestID;

        public QuestCompletedEvent(string questID)
        {
            QuestID = questID;
        }
    }
}
