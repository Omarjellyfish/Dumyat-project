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
    /// Published by <see cref="TutorialRunner"/> the moment a step's completion trigger fires — the
    /// player has DONE the thing the card asked for.
    ///
    /// The step is not over. This is what lights the card's NEXT button; the player still has to press
    /// it, and that press is what <see cref="TutorialStepCompletedEvent"/> reports. Subscribe here for
    /// a reaction that should land on the action itself (a chime, a "well done" line) rather than on
    /// the player getting around to dismissing the card.
    /// </summary>
    public readonly struct TutorialStepSatisfiedEvent : IGameEvent
    {
        public readonly string SequenceId;
        public readonly int StepIndex;

        public TutorialStepSatisfiedEvent(string sequenceId, int stepIndex)
        {
            SequenceId = sequenceId;
            StepIndex = stepIndex;
        }
    }

    /// <summary>
    /// Published by <see cref="TutorialRunner"/> when a step is left behind — i.e. the player pressed
    /// NEXT on a satisfied step. Carries timing data for analytics: the duration spans the whole card,
    /// from it appearing to it being dismissed, so it includes reading time after the action.
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
