using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Global tutorial orchestrator. Manages persistence (which tutorials have been completed)
    /// and delegates scene-side execution to the <see cref="TutorialRunner"/> found in the active scene.
    /// Registered as <see cref="ITutorialService"/> singleton in <see cref="GameInstaller"/>.
    /// </summary>
    public sealed class TutorialService : ITutorialService
    {
        private readonly IEventService _eventService;
        private readonly ISaveService _saveService;

        private TutorialRunner _activeRunner;
        private TutorialSequenceSO _activeSequence;

        public TutorialService(IEventService eventService, ISaveService saveService)
        {
            _eventService = eventService;
            _saveService = saveService;
        }

        public bool IsActive => _activeRunner != null && _activeRunner.IsRunning;

        public bool IsCompleted(string sequenceId)
        {
            if (string.IsNullOrEmpty(sequenceId)) return false;
            return _saveService.CurrentSaveData.tutorialData.CompletedSequenceIds.Contains(sequenceId);
        }

        public void StartTutorial(TutorialSequenceSO sequence, bool force = false)
        {
            if (sequence == null)
            {
                Debug.LogWarning("[TutorialService] StartTutorial called with null sequence.");
                return;
            }

            if (!force && sequence.PersistCompletion && IsCompleted(sequence.SequenceId))
            {
                Debug.Log($"[TutorialService] Tutorial '{sequence.SequenceId}' already completed. Skipping.");
                return;
            }

            if (IsActive)
            {
                Debug.LogWarning($"[TutorialService] Tutorial '{_activeSequence?.SequenceId}' is already running. " +
                                 "Stop it first or wait for completion.");
                return;
            }

            _activeRunner = Object.FindFirstObjectByType<TutorialRunner>();
            if (_activeRunner == null)
            {
                Debug.LogError("[TutorialService] No TutorialRunner found in the active scene. " +
                               "Place a TutorialRunner MonoBehaviour in scenes that need tutorials.");
                return;
            }

            _activeSequence = sequence;
            _activeRunner.Run(sequence, this);
        }

        /// <summary>
        /// Whether the player may abandon the tutorial — true whenever one is running. There is no
        /// such thing as a step you cannot skip out of: being unable to leave a tutorial is worse for
        /// a returning player than any step being missed.
        /// </summary>
        public bool CanSkip => IsActive;

        /// <summary>Whether the step on screen has been done, so NEXT may move on.</summary>
        public bool CanAdvance => IsActive && _activeRunner.CanAdvanceCurrentStep;

        public void AdvanceCurrentStep()
        {
            if (!IsActive) return;

            if (!_activeRunner.CanAdvanceCurrentStep)
            {
                // Deliberately not a warning: a locked NEXT is the design, not a fault. Logged only so
                // that "I pressed NEXT and nothing happened" is diagnosable from a console capture.
                Debug.Log($"[TutorialService] NEXT refused — step {_activeRunner.CurrentStepIndex} of " +
                          $"'{_activeSequence?.SequenceId}' has not been done yet.");
                return;
            }

            _activeRunner.AdvanceStep();
        }

        /// <summary>
        /// Force past the step on screen without doing it. This is the developer step-over — the
        /// tutorial card's NEXT button uses <see cref="AdvanceCurrentStep"/>, which insists the player
        /// actually performed the step.
        /// </summary>
        public void SkipCurrentStep()
        {
            if (!IsActive) return;
            _activeRunner.SkipStep();
        }

        public void SkipTutorial()
        {
            if (!IsActive || _activeSequence == null) return;

            int skippedAt = _activeRunner.CurrentStepIndex;
            _activeRunner.Stop();

            MarkCompleted(_activeSequence);
            _eventService.Publish(new TutorialSkippedEvent(_activeSequence.SequenceId, skippedAt));

            Debug.Log($"[TutorialService] Tutorial '{_activeSequence.SequenceId}' skipped at step {skippedAt}.");
            _activeSequence = null;
            _activeRunner = null;
        }

        public void ReplayTutorial(TutorialSequenceSO sequence)
        {
            StartTutorial(sequence, force: true);
        }

        /// <summary>Called by <see cref="TutorialRunner"/> when the sequence finishes normally.</summary>
        internal void OnSequenceCompleted(TutorialSequenceSO sequence, float totalDuration)
        {
            MarkCompleted(sequence);
            _eventService.Publish(new TutorialCompletedEvent(sequence.SequenceId, totalDuration));

            if (sequence.CompletionDialogue != null)
            {
                _eventService.Publish(new PlayDialogueEvent(sequence.CompletionDialogue, DialoguePriority.High));
            }

            Debug.Log($"[TutorialService] Tutorial '{sequence.SequenceId}' completed in {totalDuration:F1}s.");
            _activeSequence = null;
            _activeRunner = null;
        }

        private void MarkCompleted(TutorialSequenceSO sequence)
        {
            if (!sequence.PersistCompletion) return;

            var completedList = _saveService.CurrentSaveData.tutorialData.CompletedSequenceIds;
            if (!completedList.Contains(sequence.SequenceId))
            {
                completedList.Add(sequence.SequenceId);
                _saveService.SaveAsync();
            }
        }
    }
}
