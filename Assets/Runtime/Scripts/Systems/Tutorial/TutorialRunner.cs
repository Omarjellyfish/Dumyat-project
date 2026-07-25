using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// Scene-side tutorial executor. Iterates through a <see cref="TutorialSequenceSO"/>,
    /// managing the hover UI, completion triggers, and branch evaluation for each step.
    /// Place one of these in every scene that has tutorials.
    /// </summary>
    public class TutorialRunner : MonoBehaviour
    {
        [Header("Auto-Start (Optional)")]
        [Tooltip("If assigned, this tutorial starts automatically when the scene loads")]
        [SerializeField] private TutorialSequenceSO _autoStartSequence;

        [Header("Hover UI")]
        [Tooltip("The TutorialHoverUI component — can be on this GameObject or a child")]
        [SerializeField] private TutorialHoverUI _hoverUI;

        private IEventService _eventService;
        private IHapticService _hapticService;
        private TutorialService _tutorialService;

        private TutorialSequenceSO _activeSequence;
        private int _currentStepIndex;
        private float _stepStartTime;
        private float _sequenceStartTime;
        private bool _isRunning;
        private Coroutine _delayCoroutine;

        // Cached delegate for event-based completion so we can unsubscribe cleanly
        private Action<object> _eventCompletionHandler;
        private TutorialZoneSensor _activeZoneSensor;

        public bool IsRunning => _isRunning;
        public int CurrentStepIndex => _currentStepIndex;
        public TutorialSequenceSO SceneTutorial => _autoStartSequence;

        [Inject]
        private void Inject(IEventService eventService, IHapticService hapticService, ITutorialService tutorialService)
        {
            _eventService = eventService;
            _hapticService = hapticService;
            // Cache the concrete type for internal callback
            _tutorialService = tutorialService as TutorialService;
        }

        private void Start()
        {
            if (_hoverUI == null)
            {
                _hoverUI = GetComponentInChildren<TutorialHoverUI>(true);
            }

            if (_autoStartSequence != null && _tutorialService != null)
            {
                _tutorialService.StartTutorial(_autoStartSequence);
            }
        }

        /// <summary>Begin running a tutorial sequence. Called by <see cref="TutorialService"/>.</summary>
        internal void Run(TutorialSequenceSO sequence, TutorialService service)
        {
            if (sequence == null || sequence.Steps == null || sequence.Steps.Count == 0)
            {
                Debug.LogWarning("[TutorialRunner] Cannot run null or empty sequence.");
                return;
            }

            _tutorialService = service;
            _activeSequence = sequence;
            _currentStepIndex = 0;
            _isRunning = true;
            _sequenceStartTime = Time.time;

            _eventService.Publish(new TutorialStartedEvent(sequence.SequenceId, sequence.DisplayName));

            ActivateStep(_currentStepIndex);
        }

        /// <summary>Stop the tutorial immediately, cleaning up all active triggers.</summary>
        internal void Stop()
        {
            CleanupCurrentStep();

            if (_hoverUI != null)
            {
                _hoverUI.Hide();
            }

            _isRunning = false;
            _activeSequence = null;
        }

        /// <summary>Skip the current step and advance to the next.</summary>
        internal void SkipStep()
        {
            if (!_isRunning) return;
            CompleteCurrentStep();
        }

        /// <summary>
        /// Called externally (e.g. by a game mechanic script) to mark the current step as done.
        /// Only works if the step's <see cref="TutorialCompletionType"/> is <c>Manual</c>.
        /// </summary>
        public void CompleteCurrentStep()
        {
            if (!_isRunning) return;
            OnStepCompleted();
        }

        // ─────────────────────────────────────────────────────────────
        // Step Lifecycle
        // ─────────────────────────────────────────────────────────────

        private void ActivateStep(int index)
        {
            if (_activeSequence == null || index < 0 || index >= _activeSequence.Steps.Count)
            {
                OnSequenceFinished();
                return;
            }

            TutorialStepSO step = _activeSequence.Steps[index];

            // Check persistence condition — skip step if not met
            if (!string.IsNullOrEmpty(step.PersistenceConditionKey))
            {
                // TODO: When persistence key access is added to TutorialService, 
                // evaluate the key against step.RequiredState here and skip if needed.
            }

            _currentStepIndex = index;
            _stepStartTime = Time.time;

            // Show hover UI
            ShowHoverUI(step);

            // Play voice line if assigned
            if (step.VoiceLine != null)
            {
                _eventService.Publish(new PlayDialogueEvent(step.VoiceLine, DialoguePriority.Normal));
            }

            // Fire haptic on both hands if assigned
            if (step.HapticOnActivate != null)
            {
                _hapticService?.TriggerVibration(step.HapticOnActivate, HapticHand.Both);
            }

            // Publish step changed event
            _eventService.Publish(new TutorialStepChangedEvent(
                _activeSequence.SequenceId,
                _currentStepIndex,
                _activeSequence.Steps.Count,
                step));

            // Set up completion trigger
            SetupCompletionTrigger(step);
        }

        private void OnStepCompleted()
        {
            float duration = Time.time - _stepStartTime;
            CleanupCurrentStep();

            // Publish step completed event with timing
            _eventService.Publish(new TutorialStepCompletedEvent(
                _activeSequence.SequenceId,
                _currentStepIndex,
                duration));

            // Evaluate branches
            int nextIndex = EvaluateBranches(_currentStepIndex);

            if (nextIndex >= _activeSequence.Steps.Count)
            {
                OnSequenceFinished();
            }
            else
            {
                ActivateStep(nextIndex);
            }
        }

        private void OnSequenceFinished()
        {
            float totalDuration = Time.time - _sequenceStartTime;

            if (_hoverUI != null)
            {
                _hoverUI.Hide();
            }

            _isRunning = false;

            _tutorialService?.OnSequenceCompleted(_activeSequence, totalDuration);
            _activeSequence = null;
        }

        // ─────────────────────────────────────────────────────────────
        // Hover UI
        // ─────────────────────────────────────────────────────────────

        private void ShowHoverUI(TutorialStepSO step)
        {
            if (_hoverUI == null) return;

            // Find the primary anchor via registry
            Transform anchor = TutorialAnchorRegistry.GetPrimaryAnchor(step.PrimaryAnchorId);

            _hoverUI.ShowStep(
                step,
                _currentStepIndex,
                _activeSequence.Steps.Count,
                anchor,
                step.AnchorOffset);
        }

        // ─────────────────────────────────────────────────────────────
        // Completion Triggers
        // ─────────────────────────────────────────────────────────────

        private void SetupCompletionTrigger(TutorialStepSO step)
        {
            switch (step.CompletionType)
            {
                case TutorialCompletionType.Event:
                    SetupEventTrigger(step.EventTypeName);
                    break;

                case TutorialCompletionType.ZoneEnter:
                    SetupZoneTrigger(step.ZoneId);
                    break;

                case TutorialCompletionType.Delay:
                    _delayCoroutine = StartCoroutine(DelayCompletion(step.DelaySeconds));
                    break;

                case TutorialCompletionType.Manual:
                    // Nothing to set up — external code calls CompleteCurrentStep()
                    break;
            }
        }

        private void SetupEventTrigger(string eventTypeName)
        {
            if (string.IsNullOrEmpty(eventTypeName))
            {
                Debug.LogWarning("[TutorialRunner] Event completion type but no EventTypeName set.");
                return;
            }

            // Find the event type by name in the DumyatRuntime namespace
            Type eventType = Type.GetType($"DumyatRuntime.{eventTypeName}");
            if (eventType == null)
            {
                // Try without namespace qualifier
                eventType = Type.GetType(eventTypeName);
            }

            if (eventType == null)
            {
                Debug.LogError($"[TutorialRunner] Could not find event type '{eventTypeName}'. " +
                               "Make sure it's the correct type name (e.g. 'RecipeSelectedEvent').");
                return;
            }

            // Use reflection to subscribe to the event type dynamically
            // Build a generic Action<T> that calls OnStepCompleted
            var subscribeMethod = typeof(IEventService)
                .GetMethod(nameof(IEventService.Subscribe))
                ?.MakeGenericMethod(eventType);

            // // Create a delegate of the correct Action<T> type
            // var actionType = typeof(Action<>).MakeGenericType(eventType);
            // var handler = Delegate.CreateDelegate(actionType, this, nameof(OnEventFired));

            // Store for cleanup
            _eventCompletionHandler = (Action<object>)((obj) => OnStepCompleted());

            // We need a concrete typed delegate — use a lambda wrapper via DynamicInvoke approach
            var typedHandler = CreateEventHandler(eventType);
            subscribeMethod?.Invoke(_eventService, new object[] { typedHandler });
        }

        /// <summary>
        /// Creates a strongly-typed Action&lt;T&gt; delegate that calls <see cref="OnStepCompleted"/>
        /// when the event fires. The delegate is cached for cleanup.
        /// </summary>
        private Delegate CreateEventHandler(Type eventType)
        {
            // We store the delegate as object for later unsubscribe
            var method = typeof(TutorialRunner).GetMethod(nameof(HandleGenericEvent),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var genericMethod = method.MakeGenericMethod(eventType);

            var actionType = typeof(Action<>).MakeGenericType(eventType);
            var handler = Delegate.CreateDelegate(actionType, this, genericMethod);

            _cachedEventType = eventType;
            _cachedEventDelegate = handler;

            return handler;
        }

        private Type _cachedEventType;
        private Delegate _cachedEventDelegate;

        private void HandleGenericEvent<T>(T evt) where T : IGameEvent
        {
            OnStepCompleted();
        }

        // Dummy target for delegate creation — not called directly
        private void OnEventFired(object evt)
        {
            OnStepCompleted();
        }

        private void CleanupEventTrigger()
        {
            if (_cachedEventType != null && _cachedEventDelegate != null)
            {
                var unsubscribeMethod = typeof(IEventService)
                    .GetMethod(nameof(IEventService.Unsubscribe))
                    ?.MakeGenericMethod(_cachedEventType);

                unsubscribeMethod?.Invoke(_eventService, new object[] { _cachedEventDelegate });

                _cachedEventType = null;
                _cachedEventDelegate = null;
            }
        }

        private void SetupZoneTrigger(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId))
            {
                Debug.LogWarning("[TutorialRunner] ZoneEnter completion type but no ZoneId set.");
                return;
            }

            Transform zoneAnchor = TutorialAnchorRegistry.GetPrimaryAnchor(zoneId);
            if (zoneAnchor == null)
            {
                Debug.LogError($"[TutorialRunner] No TutorialAnchor found with ID '{zoneId}' for zone trigger.");
                return;
            }

            _activeZoneSensor = zoneAnchor.GetComponent<TutorialZoneSensor>();
            if (_activeZoneSensor == null)
            {
                _activeZoneSensor = zoneAnchor.gameObject.AddComponent<TutorialZoneSensor>();
            }

            _activeZoneSensor.Activate(OnStepCompleted);
        }

        private IEnumerator DelayCompletion(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            OnStepCompleted();
        }

        private void CleanupCurrentStep()
        {
            // Stop delay coroutine
            if (_delayCoroutine != null)
            {
                StopCoroutine(_delayCoroutine);
                _delayCoroutine = null;
            }

            // Unsubscribe event trigger
            CleanupEventTrigger();

            // Deactivate zone sensor
            if (_activeZoneSensor != null)
            {
                _activeZoneSensor.Deactivate();
                _activeZoneSensor = null;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Branch Evaluation
        // ─────────────────────────────────────────────────────────────

        private int EvaluateBranches(int completedStepIndex)
        {
            if (_activeSequence.Branches == null || _activeSequence.Branches.Count == 0)
            {
                return completedStepIndex + 1;
            }

            foreach (var branch in _activeSequence.Branches)
            {
                if (branch.AfterStepIndex != completedStepIndex) continue;

                // For now, branch evaluation is simplified:
                // If a branch exists for this step, it jumps unconditionally.
                // Full condition evaluation (checking event payloads) can be added
                // by storing the last event result and comparing.
                // The most common use case is: branch on validation pass/fail,
                // which the CraftingValidatedEvent already carries as ValidationResult.
                return branch.JumpToStepIndex;
            }

            return completedStepIndex + 1;
        }

        private void OnDisable()
        {
            if (_isRunning)
            {
                CleanupCurrentStep();
            }
        }
    }
}
