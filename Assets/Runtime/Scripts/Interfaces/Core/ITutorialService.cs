namespace DumyatRuntime
{
    /// <summary>
    /// Contract for the tutorial orchestration service.
    /// Registered as a DI singleton in <see cref="GameInstaller"/>.
    /// Manages persistence (which tutorials are completed) and delegates
    /// scene-side execution to <see cref="TutorialRunner"/>.
    /// </summary>
    public interface ITutorialService
    {
        /// <summary>
        /// Begin a tutorial sequence. No-op if already completed unless
        /// <paramref name="force"/> is true (used by replay).
        /// </summary>
        void StartTutorial(TutorialSequenceSO sequence, bool force = false);

        /// <summary>
        /// The player pressed NEXT — the tutorial card's route forward. Moves on once the step's
        /// action has happened, or immediately on a <see cref="TutorialNextButton.Always"/> step.
        /// </summary>
        void AdvanceCurrentStep();

        /// <summary>
        /// Whether NEXT should be live. The card mirrors this into the button's enabled state; the
        /// service enforces it regardless.
        /// </summary>
        bool CanAdvance { get; }

        /// <summary>
        /// Force past the current step without doing it — the developer step-over, not the card's
        /// NEXT button (that is <see cref="AdvanceCurrentStep"/>).
        /// </summary>
        void SkipCurrentStep();

        /// <summary>Abandon the entire active tutorial. Marks it completed. Always allowed.</summary>
        void SkipTutorial();

        /// <summary>
        /// Whether the player may abandon the tutorial — true whenever one is running. Kept as a
        /// property so UI can bind to it, but there is no step that refuses a skip.
        /// </summary>
        bool CanSkip { get; }

        /// <summary>Replay a previously completed tutorial, ignoring persistence.</summary>
        void ReplayTutorial(TutorialSequenceSO sequence);

        /// <summary>True when a tutorial sequence is currently running.</summary>
        bool IsActive { get; }

        /// <summary>Check if a specific tutorial has been completed before.</summary>
        bool IsCompleted(string sequenceId);
    }
}
