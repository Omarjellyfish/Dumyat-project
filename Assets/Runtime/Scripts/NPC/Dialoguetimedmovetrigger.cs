using System.Collections;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Starts a countdown the moment a specific dialogue sequence begins playing, then moves a
    /// character once that time elapses. Does NOT wait for any "dialogue finished" event - just a
    /// plain timer, since nothing in the project currently reports when a sequence finishes.
    ///
    /// Set _waitSeconds to roughly how long the dialogue sequence takes to play out (sum of its
    /// lines' durations, or just eyeball it and adjust after testing).
    /// </summary>
    public class DialogueTimedMoveTrigger : MonoBehaviour
    {
        [Tooltip("The dialogue sequence to watch for. Should match what NarrativeTriggerZone (or " +
                 "anything else) publishes via PlayDialogueEvent.")]
        [SerializeField] private DialogueSequenceSO _dialogueSequence;

        [Tooltip("Character to move once the timer elapses.")]
        [SerializeField] private CharacterMoveOnly _characterToMove;

        [Tooltip("Seconds to wait after the dialogue STARTS before moving. Set this to roughly the " +
                 "sequence's total playback length.")]
        [SerializeField] private float _waitSeconds = 10f;

        [Tooltip("If true, only reacts once. If false, restarts the timer every time this sequence " +
                 "plays again.")]
        [SerializeField] private bool _triggerOnce = true;

        private IEventService _eventService;
        private bool _hasFired;
        private Coroutine _waitRoutine;

        [Inject]
        private void Inject(IEventService eventService)
        {
            _eventService = eventService;
        }

        private void OnEnable()
        {
            if (_eventService == null)
            {
                Debug.LogError($"[{name}] _eventService is NULL - injection likely hasn't run yet " +
                                "(OnEnable fired before Inject). If this happens, subscribe from " +
                                "Start() instead.", this);
                return;
            }

            _eventService.Subscribe<PlayDialogueEvent>(OnPlayDialogueEvent);
        }

        private void OnDisable()
        {
            _eventService?.Unsubscribe<PlayDialogueEvent>(OnPlayDialogueEvent);

            if (_waitRoutine != null)
            {
                StopCoroutine(_waitRoutine);
                _waitRoutine = null;
            }
        }

        private void OnPlayDialogueEvent(PlayDialogueEvent evt)
        {
            if (_triggerOnce && _hasFired) return;

            if (_dialogueSequence == null || evt.Sequence != _dialogueSequence) return;

            if (_characterToMove == null)
            {
                Debug.LogWarning($"[{name}] Dialogue started but no CharacterToMove assigned.", this);
                return;
            }

            if (_waitRoutine != null)
            {
                StopCoroutine(_waitRoutine);
            }

            Debug.Log($"[DialogueTimedMoveTrigger] {_dialogueSequence.name} started, will move " +
                      $"{_characterToMove.name} in {_waitSeconds}s.");
            _waitRoutine = StartCoroutine(WaitThenMove());
        }

        private IEnumerator WaitThenMove()
        {
            yield return new WaitForSeconds(_waitSeconds);
            _waitRoutine = null;
            _hasFired = true;

            Debug.Log($"[DialogueTimedMoveTrigger] Timer elapsed, moving {_characterToMove.name}");
            _characterToMove.Move();
        }
    }
}