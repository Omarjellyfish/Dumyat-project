using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// Optional analytics component that logs tutorial events to the console.
    /// Drop this in any scene to track player progress through tutorials.
    /// Can be extended to write to a file or send to an analytics service.
    /// </summary>
    public class TutorialDataLogger : MonoBehaviour
    {
        private IEventService _eventService;

        [Inject]
        private void Inject(IEventService eventService)
        {
            _eventService = eventService;
        }

        private void OnEnable()
        {
            _eventService.Subscribe<TutorialStartedEvent>(OnTutorialStarted);
            _eventService.Subscribe<TutorialStepCompletedEvent>(OnStepCompleted);
            _eventService.Subscribe<TutorialCompletedEvent>(OnTutorialCompleted);
            _eventService.Subscribe<TutorialSkippedEvent>(OnTutorialSkipped);
        }

        private void OnDisable()
        {
            _eventService.Unsubscribe<TutorialStartedEvent>(OnTutorialStarted);
            _eventService.Unsubscribe<TutorialStepCompletedEvent>(OnStepCompleted);
            _eventService.Unsubscribe<TutorialCompletedEvent>(OnTutorialCompleted);
            _eventService.Unsubscribe<TutorialSkippedEvent>(OnTutorialSkipped);
        }

        private void OnTutorialStarted(TutorialStartedEvent evt)
        {
            Debug.Log($"[TutorialLog] STARTED: '{evt.DisplayName}' (id: {evt.SequenceId})");
        }

        private void OnStepCompleted(TutorialStepCompletedEvent evt)
        {
            Debug.Log($"[TutorialLog] STEP {evt.StepIndex} completed in {evt.DurationSeconds:F1}s " +
                      $"(sequence: {evt.SequenceId})");
        }

        private void OnTutorialCompleted(TutorialCompletedEvent evt)
        {
            Debug.Log($"[TutorialLog] COMPLETED: '{evt.SequenceId}' — total time: {evt.TotalDurationSeconds:F1}s");
        }

        private void OnTutorialSkipped(TutorialSkippedEvent evt)
        {
            Debug.Log($"[TutorialLog] SKIPPED: '{evt.SequenceId}' at step {evt.SkippedAtStep}");
        }
    }
}
