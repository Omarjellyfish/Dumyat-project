namespace DumyatRuntime
{
    /// <summary>Published by <see cref="TutorialRunner"/> when a tutorial sequence begins.</summary>
    public readonly struct TutorialStartedEvent : IGameEvent
    {
        public readonly string SequenceId;
        public readonly string DisplayName;

        public TutorialStartedEvent(string sequenceId, string displayName)
        {
            SequenceId = sequenceId;
            DisplayName = displayName;
        }
    }

    /// <summary>Published by <see cref="TutorialRunner"/> when the active step changes.</summary>
    public readonly struct TutorialStepChangedEvent : IGameEvent
    {
        public readonly string SequenceId;
        public readonly int StepIndex;
        public readonly int TotalSteps;
        public readonly TutorialStepSO Step;

        public TutorialStepChangedEvent(string sequenceId, int stepIndex, int totalSteps, TutorialStepSO step)
        {
            SequenceId = sequenceId;
            StepIndex = stepIndex;
            TotalSteps = totalSteps;
            Step = step;
        }
    }

    /// <summary>
    /// Published by <see cref="TutorialRunner"/> when a step completes.
    /// Carries timing data for analytics.
    /// </summary>
    public readonly struct TutorialStepCompletedEvent : IGameEvent
    {
        public readonly string SequenceId;
        public readonly int StepIndex;
        public readonly float DurationSeconds;

        public TutorialStepCompletedEvent(string sequenceId, int stepIndex, float durationSeconds)
        {
            SequenceId = sequenceId;
            StepIndex = stepIndex;
            DurationSeconds = durationSeconds;
        }
    }

    /// <summary>Published by <see cref="TutorialRunner"/> when the entire sequence finishes successfully.</summary>
    public readonly struct TutorialCompletedEvent : IGameEvent
    {
        public readonly string SequenceId;
        public readonly float TotalDurationSeconds;

        public TutorialCompletedEvent(string sequenceId, float totalDurationSeconds)
        {
            SequenceId = sequenceId;
            TotalDurationSeconds = totalDurationSeconds;
        }
    }

    /// <summary>Published by <see cref="TutorialService"/> when the player skips a tutorial.</summary>
    public readonly struct TutorialSkippedEvent : IGameEvent
    {
        public readonly string SequenceId;
        public readonly int SkippedAtStep;

        public TutorialSkippedEvent(string sequenceId, int skippedAtStep)
        {
            SequenceId = sequenceId;
            SkippedAtStep = skippedAtStep;
        }
    }
}
