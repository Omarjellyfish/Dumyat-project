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

        /// <summary>Skip the current step (advances to next). No-op on a mandatory step.</summary>
        void SkipCurrentStep();

        /// <summary>Skip the entire active tutorial. Marks as completed. No-op on a mandatory step.</summary>
        void SkipTutorial();

        /// <summary>
        /// Whether the step on screen may be skipped — false while a step with
        /// <see cref="TutorialStepSO.Skippable"/> unchecked is showing. UI should hide its skip
        /// controls when this is false; both skip methods enforce it independently anyway.
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
