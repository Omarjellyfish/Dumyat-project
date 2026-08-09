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
    ///
    /// A step has two distinct moments, and keeping them apart is the point of this class:
    ///
    ///   SATISFIED — the completion trigger fired; the player has done what the card asked.
    ///               <see cref="MarkStepSatisfied"/>. Unlocks NEXT. Does NOT advance.
    ///   COMPLETED — the player pressed NEXT on a satisfied step. <see cref="AdvanceStep"/>.
    ///
    /// They used to be the same instant: any trigger auto-advanced, and the NEXT button was wired
    /// straight to the skip path, so a player could click through an entire tutorial without touching
    /// a tool. Splitting them is what makes NEXT mean "I've done this and I'm ready" instead of
    /// "get out of my way".
    /// </summary>
    public class TutorialRunner : MonoBehaviour
    {
        [Header("Auto-Start (Optional)")]
        [Tooltip("If assigned, this tutorial starts automatically when the scene loads")]
        [SerializeField] private TutorialSequenceSO _autoStartSequence;

        [Header("Hover UI")]
        [Tooltip("The TutorialHoverUI component — can be on this GameObject or a child")]
        [SerializeField] private TutorialHoverUI _hoverUI;

        [Header("Highlights")]
        [Tooltip("Optional prefab instantiated as a child of highlighted anchors. Assign it to use " +
                 "your own highlight art INSTEAD of the built-in beacon/frame below.")]
        [SerializeField] private GameObject _highlightPrefab;

        [Tooltip("Draws the built-in beacon (places, objects) or frame (UI windows) a step's " +
                 "PrimaryAnchorId / HighlightIds ask for. Auto-added to this GameObject if missing, " +
                 "so no scene has to remember to wire it. Ignored when a Highlight Prefab is assigned.")]
        [SerializeField] private TutorialHighlighter _highlighter;

        [Header("Safety")]
        [Tooltip("If a step's completion trigger has not fired after this long, NEXT unlocks anyway so " +
                 "the player is never trapped on a card they cannot dismiss. 0 disables it. Any step " +
                 "can override it with TutorialStepSO.FallbackUnlockSeconds.")]
        [SerializeField, Min(0f)] private float _defaultFallbackUnlockSeconds = 120f;

        private IEventService _eventService;
        private IHapticService _hapticService;
        private TutorialService _tutorialService;
        private ISoundService _soundService;
        private ISceneLoadService _sceneLoadService;

        private TutorialSequenceSO _activeSequence;
        private int _currentStepIndex;
        private float _stepStartTime;
        private float _sequenceStartTime;
        private bool _isRunning;
        private bool _stepSatisfied;
        private Coroutine _delayCoroutine;
        private Coroutine _fallbackCoroutine;

        private List<GameObject> _spawnedHighlights = new List<GameObject>();
        private List<TutorialAnchor> _currentlyHighlightedAnchors = new List<TutorialAnchor>();

        // Reused across steps rather than allocated per step — ApplyHighlights runs on every step
        // change, and the list is fully rewritten by Collect each time.
        private readonly List<TutorialAnchor> _highlightTargets = new List<TutorialAnchor>();

        private TutorialZoneSensor _activeZoneSensor;

        public bool IsRunning => _isRunning;
        public int CurrentStepIndex => _currentStepIndex;
        public TutorialSequenceSO SceneTutorial => _autoStartSequence;

        /// <summary>The step on screen right now, or null when nothing is running.</summary>
        public TutorialStepSO CurrentStep =>
            _isRunning && _activeSequence != null &&
            _currentStepIndex >= 0 && _currentStepIndex < _activeSequence.Steps.Count
                ? _activeSequence.Steps[_currentStepIndex]
                : null;

        /// <summary>
        /// Whether NEXT may move on from the step on screen — either because the player has done what
        /// it asked, or because it is an <see cref="TutorialNextButton.Always"/> step, which by
        /// definition lets them leave without the game having observed anything.
        /// </summary>
        public bool CanAdvanceCurrentStep
        {
            get
            {
                if (!_isRunning) return false;
                if (_stepSatisfied) return true;

                TutorialStepSO step = CurrentStep;
                return step != null && step.NextButton == TutorialNextButton.Always;
            }
        }

        [Inject]
        private void Inject(IEventService eventService, IHapticService hapticService, ITutorialService tutorialService,
                            ISoundService soundService, ISceneLoadService sceneLoadService)
        {
            _eventService = eventService;
            _hapticService = hapticService;
            // Cache the concrete type for internal callback
            _tutorialService = tutorialService as TutorialService;
            _soundService = soundService;
            _sceneLoadService = sceneLoadService;
        }

        private void Start()
        {
            if (_hoverUI == null)
            {
                _hoverUI = GetComponentInChildren<TutorialHoverUI>(true);
            }

            // Added rather than merely warned about: highlighting is driven entirely by data on the
            // step assets, so a runner without this component silently ignores instructions that are
            // already authored on every existing step.
            if (_highlighter == null)
            {
                _highlighter = GetComponentInChildren<TutorialHighlighter>(true)
                               ?? gameObject.AddComponent<TutorialHighlighter>();
            }

            if (_autoStartSequence == null || _tutorialService == null) return;

            // A scene entered by TRAVEL has not placed the player yet. FadeTransitionHandler and
            // PlayerRigSpawner both position the rig on SceneLoadCompleteEvent, which SceneLoadService
            // publishes AFTER every new-scene Start has run — so for at least one frame (and one
            // physics tick) the rig is still standing at its DEPARTURE scene's world coordinates.
            //
            // Arming a ZoneEnter step here therefore tests it against those stale coordinates. That is
            // why arriving in the workshop from First_room skipped straight to the second tip while
            // arriving by bus did not: First_room's interior happens to map inside carpentar_room's
            // WorkshopZone box, so the brand-new trigger reported an overlap the instant it existed.
            // The bus stops sit far outside it, so the same code looked correct from there.
            //
            // Waiting for the placement makes every route behave the same, in every scene.
            if (_sceneLoadService != null && _sceneLoadService.IsLoading)
            {
                _eventService.Subscribe<SceneLoadCompleteEvent>(HandlePlayerPlaced);
                return;
            }

            _tutorialService.StartTutorial(_autoStartSequence);
        }

        // Opening a scene directly in the editor never goes through a load, so that path starts above
        // and this one only ever runs for a travelled-into scene.
        private void HandlePlayerPlaced(SceneLoadCompleteEvent evt)
        {
            _eventService.Unsubscribe<SceneLoadCompleteEvent>(HandlePlayerPlaced);
            StartCoroutine(StartWhenPhysicsSettled());
        }

        // One fixed step after the teleport, so the physics engine has flushed the exit/enter
        // bookkeeping for the rig's jump before any zone sensor is armed against it.
        private IEnumerator StartWhenPhysicsSettled()
        {
            yield return new WaitForFixedUpdate();
            _tutorialService?.StartTutorial(_autoStartSequence);
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
            _stepSatisfied = false;
            _activeSequence = null;
        }

        /// <summary>
        /// Force past the current step whether or not the player did it. This is the developer
        /// step-over, reached through <c>ITutorialService.SkipCurrentStep</c>. The card's NEXT button
        /// does NOT come through here — it uses <see cref="AdvanceStep"/>, which insists the step was
        /// actually satisfied.
        /// </summary>
        internal void SkipStep()
        {
            if (!_isRunning) return;
            OnStepCompleted();
        }

        /// <summary>
        /// The player pressed NEXT. Refuses until the step's action has actually happened — the card
        /// keeps NEXT disabled until then too, so reaching this in an unsatisfied state means
        /// something called it directly.
        /// </summary>
        internal void AdvanceStep()
        {
            if (!CanAdvanceCurrentStep) return;
            OnStepCompleted();
        }

        /// <summary>
        /// Answer the current step from a game mechanic — the escape hatch for an action that has no
        /// event worth publishing. Equivalent to that step's trigger firing: it unlocks NEXT, it does
        /// not advance.
        /// </summary>
        public void MarkCurrentStepSatisfied() => MarkStepSatisfied();

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

            _currentStepIndex = index;
            _stepStartTime = Time.time;

            // Cleared before the card is drawn, so it opens with NEXT locked rather than inheriting
            // the previous step's unlocked state for a frame.
            _stepSatisfied = false;

            // Show hover UI
            ShowHoverUI(step);

            // Play voice line if assigned
            if (step.VoiceLine != null)
            {
                _eventService.Publish(new PlayDialogueEvent(step.VoiceLine, DialoguePriority.Normal));
            }

            // Play direct audio clip if assigned
            if (step.Audio != null && _soundService != null)
            {
                _soundService.PlayVoiceLine(step.Audio, _hoverUI != null ? _hoverUI.transform : transform, 1f);
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

            // Raise this step's highlights. After ShowHoverUI so the card and the highlight appear
            // on the same frame.
            ApplyHighlights(step);

            // Arm the trigger that unlocks NEXT. ReadAndContinue satisfies synchronously in here, so
            // nothing below may assume the step is still locked.
            SetupCompletionTrigger(step);

            ArmFallbackUnlock(step);
        }

        /// <summary>
        /// The step's action has happened. Lights NEXT and stops listening; deliberately does NOT
        /// advance — the player presses NEXT when they are ready, which is the whole point of the gate.
        /// Idempotent, because a trigger firing more than once is normal (a second fish, a second cast).
        /// </summary>
        private void MarkStepSatisfied()
        {
            if (!_isRunning || _stepSatisfied) return;
            _stepSatisfied = true;

            // Triggers only — the highlights stay up, because the card is still on screen talking
            // about that object and dropping its beacon mid-read would look like a glitch.
            //
            // This commonly runs INSIDE an EventService dispatch (HandleGenericEvent), and both
            // unsubscribes and publishes from there. That is safe: EventService buffers subscription
            // changes while a publish is in flight and applies them once dispatch unwinds.
            CleanupTriggers();

            _hoverUI?.SetStepSatisfied(true);

            _eventService?.Publish(new TutorialStepSatisfiedEvent(_activeSequence.SequenceId, _currentStepIndex));

            // A step with no NEXT button has nothing left to wait for: the action the card asked for
            // has happened, and there is no control for the player to press. Doing anything else here
            // would strand them on a card with no way out.
            TutorialStepSO step = CurrentStep;
            if (step != null && ResolveNextButton(step) == TutorialNextButton.Hidden)
            {
                OnStepCompleted();
            }
        }

        /// <summary>
        /// Unlocks NEXT after a while even though the trigger never fired. Every trigger here depends
        /// on something outside this class — an event another system has to publish, a collider the
        /// player has to walk into — and when one of those is missing in a particular scene the step
        /// would otherwise have no exit at all on a mandatory card. Better a tutorial that lets you
        /// past than one that ends the session.
        /// </summary>
        private void ArmFallbackUnlock(TutorialStepSO step)
        {
            if (_stepSatisfied) return;

            float seconds = step.FallbackUnlockSeconds > 0f
                ? step.FallbackUnlockSeconds
                : _defaultFallbackUnlockSeconds;

            if (seconds <= 0f) return;

            _fallbackCoroutine = StartCoroutine(FallbackUnlockAfter(seconds, step));
        }

        private IEnumerator FallbackUnlockAfter(float seconds, TutorialStepSO step)
        {
            yield return new WaitForSeconds(seconds);
            _fallbackCoroutine = null;

            Debug.LogWarning($"[TutorialRunner] Step '{step.name}' was never satisfied after {seconds:F0}s — " +
                             "unlocking NEXT so the player is not stuck. Its completion trigger " +
                             "probably cannot fire in this scene.", step);
            MarkStepSatisfied();
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
            _stepSatisfied = false;

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

            // A missing PrimaryAnchorId used to be silent, unlike a missing HighlightId — and silent
            // is exactly wrong, because the failure is a card that quietly stops following anything
            // and hangs wherever it was last placed. 'Hands' and 'Inventory' in particular ride in on
            // the XR rig and are simply absent from some scenes.
            if (!string.IsNullOrEmpty(step.PrimaryAnchorId) && anchor == null)
            {
                Debug.LogWarning($"[TutorialRunner] Step '{step.name}' places its card at anchor " +
                                 $"'{step.PrimaryAnchorId}', but no TutorialAnchor with that id is active " +
                                 $"in scene '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}'. " +
                                 "The card will not follow anything.", step);
            }

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

        // Every arm path below ends in "satisfied", never in "advanced" — and every path that CANNOT be
        // armed satisfies immediately. A trigger that could not be set up used to strand the step while
        // NEXT still worked; now that NEXT is gated, failing closed would be a dead session.
        private void SetupCompletionTrigger(TutorialStepSO step)
        {
            switch (step.CompletionType)
            {
                case TutorialCompletionType.Event:
                    SetupEventTrigger(step.EventTypeName, step);
                    break;

                case TutorialCompletionType.ZoneEnter:
                    SetupZoneTrigger(step.ZoneId, step);
                    break;

                case TutorialCompletionType.Delay:
                    _delayCoroutine = StartCoroutine(DelaySatisfy(step.DelaySeconds));
                    break;

                case TutorialCompletionType.ReadAndContinue:
                    // Nothing to wait for — this card is narration, so NEXT is live at once.
                    MarkStepSatisfied();
                    break;
            }
        }

        /// <summary>
        /// ReadAndContinue satisfies the instant the card opens, and a Hidden step advances the
        /// instant it is satisfied — together they would flash the card past unread AND re-enter
        /// ActivateStep from inside itself. There is no sensible reading of that pairing, so it is
        /// coerced to Always: the card stays up with a live NEXT.
        /// </summary>
        private static TutorialNextButton ResolveNextButton(TutorialStepSO step)
        {
            if (step.NextButton == TutorialNextButton.Hidden &&
                step.CompletionType == TutorialCompletionType.ReadAndContinue)
            {
                Debug.LogWarning($"[TutorialRunner] Step '{step.name}' hides NEXT but has nothing to wait " +
                                 "for (ReadAndContinue), so it would vanish the moment it appeared. " +
                                 "Showing NEXT instead — give it a real completion trigger, or set " +
                                 "Next Button to Always.", step);
                return TutorialNextButton.Always;
            }

            return step.NextButton;
        }

        private void SetupEventTrigger(string eventTypeName, TutorialStepSO step)
        {
            if (string.IsNullOrEmpty(eventTypeName))
            {
                Debug.LogError($"[TutorialRunner] Step '{step.name}' uses Event completion but names no " +
                               "EventTypeName. Unlocking NEXT immediately so it is not a dead end.", step);
                MarkStepSatisfied();
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
                // Type.GetType searches the calling assembly (Project.Runtime) and corelib only, so an
                // event that lives outside Assets/Runtime/Scripts/Events will never resolve here
                // regardless of spelling.
                Debug.LogError($"[TutorialRunner] Step '{step.name}' waits on event type '{eventTypeName}', " +
                               "which does not exist in Project.Runtime. Check the spelling (e.g. " +
                               "'RecipeSelectedEvent'). Unlocking NEXT so the step is not a dead end.", step);
                MarkStepSatisfied();
                return;
            }

            // Use reflection to subscribe to the event type dynamically
            // Build a generic Action<T> that calls OnStepCompleted
            var subscribeMethod = typeof(IEventService)
                .GetMethod(nameof(IEventService.Subscribe))
                ?.MakeGenericMethod(eventType);

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
            MarkStepSatisfied();
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

        private void SetupZoneTrigger(string zoneId, TutorialStepSO step)
        {
            if (string.IsNullOrEmpty(zoneId))
            {
                Debug.LogError($"[TutorialRunner] Step '{step.name}' uses ZoneEnter completion but names " +
                               "no ZoneId. Unlocking NEXT so it is not a dead end.", step);
                MarkStepSatisfied();
                return;
            }

            Transform zoneAnchor = TutorialAnchorRegistry.GetPrimaryAnchor(zoneId);
            if (zoneAnchor == null)
            {
                Debug.LogError($"[TutorialRunner] Step '{step.name}' waits on zone '{zoneId}', but no " +
                               "TutorialAnchor with that id is active in scene " +
                               $"'{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}'. " +
                               "Unlocking NEXT so it is not a dead end.", step);
                MarkStepSatisfied();
                return;
            }

            _activeZoneSensor = zoneAnchor.GetComponent<TutorialZoneSensor>();
            if (_activeZoneSensor == null)
            {
                _activeZoneSensor = zoneAnchor.gameObject.AddComponent<TutorialZoneSensor>();
            }

            _activeZoneSensor.Activate(MarkStepSatisfied);
        }

        private IEnumerator DelaySatisfy(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            _delayCoroutine = null;
            MarkStepSatisfied();
        }

        /// <summary>
        /// Drops everything that could still satisfy the current step, leaving its highlights alone.
        /// Split out from <see cref="CleanupCurrentStep"/> because satisfying a step stops the
        /// listening but keeps the card — and its beacon — on screen until the player presses NEXT.
        /// Idempotent.
        /// </summary>
        private void CleanupTriggers()
        {
            if (_delayCoroutine != null)
            {
                StopCoroutine(_delayCoroutine);
                _delayCoroutine = null;
            }

            if (_fallbackCoroutine != null)
            {
                StopCoroutine(_fallbackCoroutine);
                _fallbackCoroutine = null;
            }

            CleanupEventTrigger();

            if (_activeZoneSensor != null)
            {
                _activeZoneSensor.Deactivate();
                _activeZoneSensor = null;
            }
        }

        private void CleanupCurrentStep()
        {
            CleanupTriggers();
            ClearHighlights();
        }

        // ─────────────────────────────────────────────────────────────
        // Highlights
        //
        // Three layers, applied together, because they answer different needs:
        //
        //  1. TutorialAnchor.OnHighlightChanged — a per-object hook, so a specific anchor can light
        //     its own outline or play its own animation. Always fired.
        //  2. _highlightPrefab — custom highlight ART, when a designer has authored some.
        //  3. TutorialHighlighter — the built-in procedural beacon (places, objects) and frame (UI
        //     windows). Used only when there is no prefab, so assigning one is a real override
        //     rather than something that draws two highlights on top of each other.
        //
        // A step that asks the player to GO somewhere shows the way by naming WAYPOINT anchors in its
        // HighlightIds alongside the destination — 'Cross the Street' lists WorkshopApproach then
        // WorkshopEntrance, and its text reads "follow the markers". That is the project's route
        // pattern: a chain of ordinary beacons, authored in the scene. No separate path system.
        // ─────────────────────────────────────────────────────────────

        private void ApplyHighlights(TutorialStepSO step)
        {
            ClearHighlights();

            // Layer 3. Skipped when custom art is assigned — the prefab loop below covers it.
            if (_highlightPrefab == null) _highlighter?.Apply(step);

            // Which anchors a step highlights — and the warning when it names one that is not here —
            // is TutorialHighlightTargets' single answer, shared with TutorialHighlighter so the two
            // layers cannot disagree about what is being pointed at. See that class for why
            // PrimaryAnchorId is deliberately NOT among them.
            TutorialHighlightTargets.Collect(step, _highlightTargets, this);

            foreach (var anchor in _highlightTargets)
            {
                if (anchor == null || _currentlyHighlightedAnchors.Contains(anchor)) continue;
                
                _currentlyHighlightedAnchors.Add(anchor);
                
                // Fire event on the anchor
                anchor.SetHighlight(true);
                
                // Spawn optional prefab
                if (_highlightPrefab != null)
                {
                    var instance = Instantiate(_highlightPrefab, anchor.transform.position, Quaternion.identity, anchor.transform);
                    _spawnedHighlights.Add(instance);
                }
            }
        }

        private void ClearHighlights()
        {
            // Dropped before the next step raises its own, so the two never overlap.
            _highlighter?.Clear();

            foreach (var anchor in _currentlyHighlightedAnchors)
            {
                if (anchor != null)
                {
                    anchor.SetHighlight(false);
                }
            }
            _currentlyHighlightedAnchors.Clear();

            foreach (var instance in _spawnedHighlights)
            {
                if (instance != null)
                {
                    Destroy(instance);
                }
            }
            _spawnedHighlights.Clear();
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
            // The deferred auto-start may still be waiting on the load; dropping it here stops a
            // destroyed runner from being handed a scene-load it can no longer act on.
            _eventService?.Unsubscribe<SceneLoadCompleteEvent>(HandlePlayerPlaced);

            if (_isRunning)
            {
                CleanupCurrentStep();
            }
        }
    }
}
