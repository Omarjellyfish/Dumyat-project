using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Serialization;

namespace DumyatRuntime
{
    /// <summary>
    /// Plays a <see cref="DialogueSequenceSO"/> when the player walks into this trigger.
    ///
    /// Two independent "only once" switches, because they answer different questions:
    ///
    /// • <b>Trigger Once Per Scene</b> — a bool on this component. It forgets itself whenever the scene
    ///   is reloaded or re-entered, so the line comes back on the player's next visit. Right for a
    ///   remark that belongs to arriving somewhere.
    ///
    /// • <b>Trigger Once Per Game</b> — remembered for the whole run, keyed on the SEQUENCE rather than
    ///   on this object. Leaving the town and coming back, or hearing the same sequence from a zone in
    ///   another scene, will not replay it; quitting and relaunching will. Right for a line that
    ///   introduces something once.
    ///
    /// Neither survives quitting. For a line that must never be heard twice across sessions, use
    /// <see cref="OneShotDialogueCue"/> with <see cref="CueScope.SaveFile"/>, which writes to the save.
    /// </summary>
    public class NarrativeTriggerZone : MonoBehaviour
    {
        [SerializeField] private DialogueSequenceSO _dialogueSequence;
        [SerializeField] private DialoguePriority _priority = DialoguePriority.Normal;

        [Tooltip("Play at most once per visit to this scene. Reloading or re-entering the scene arms " +
                 "it again.")]
        [FormerlySerializedAs("_triggerOnce")]
        [SerializeField] private bool _triggerOncePerScene = true;

        [Tooltip("Play at most once for the whole run of the game. Survives changing scenes and coming " +
                 "back — including a different zone pointed at the same sequence — but resets when the " +
                 "game is restarted.\n\n" +
                 "Keyed on the sequence's Sequence Id, so give the asset one if it does not have it.")]
        [SerializeField] private bool _triggerOncePerGame = false;

        private IEventService _eventService;
        private IPlayerPersistenceService _playerPresistenceService;

        [Inject]
        private void Inject(IEventService eventService, IPlayerPersistenceService playerPersistenceService)
        {
            _eventService = eventService;
            _playerPresistenceService = playerPersistenceService;
        }

        [SerializeField] private string _persistenceKey = "";
        [SerializeField] private bool _requireKeyToTrigger = false;
        private bool _hasFired;

        /// <summary>
        /// What the per-game switch remembers. The sequence, not this object — the whole point is that
        /// the same line does not come back from somewhere else.
        /// </summary>
        private string SessionKey =>
            _dialogueSequence == null ? null
            : string.IsNullOrWhiteSpace(_dialogueSequence.SequenceId)
                ? "seq:" + _dialogueSequence.name
                : "seq:" + _dialogueSequence.SequenceId;

        private void OnTriggerEnter(Collider other)
        {
            VerboseLog.Trace($"[NarrativeTriggerZone] OnTriggerEnter entered by {other.name} (Tag: {other.tag})");

            if (!other.CompareTag("Player")) return;

            VerboseLog.Trace($"[NarrativeTriggerZone] Player detected. _hasFired: {_hasFired}");
            if (_triggerOncePerScene && _hasFired) return;

            if (_triggerOncePerGame && OnceOnlyCueGate.PlayedThisSession(SessionKey))
            {
                VerboseLog.Trace($"[NarrativeTriggerZone] '{SessionKey}' already played this run — skipping.");
                return;
            }

            if (!string.IsNullOrEmpty(_persistenceKey))
            {
                bool hasKey = _playerPresistenceService.HasKey(_persistenceKey);
                VerboseLog.Trace($"[NarrativeTriggerZone] Checking persistence key: {_persistenceKey}. HasKey: {hasKey}. Required: {_requireKeyToTrigger}");
                if (hasKey != _requireKeyToTrigger) return;
            }

            if (_dialogueSequence == null)
            {
                Debug.LogWarning($"[{name}] No DialogueSequenceSO assigned.", this);
                return;
            }

            _hasFired = true;
            VerboseLog.Trace($"[NarrativeTriggerZone] Publishing PlayDialogueEvent for sequence: {_dialogueSequence.name}");

            if (_eventService == null)
            {
                Debug.LogError($"[{name}] _eventService is NULL! Injection failed.");
                return;
            }

            // Marked only once the line is actually going out, so a zone that bailed above stays armed.
            if (_triggerOncePerGame) OnceOnlyCueGate.MarkPlayedThisSession(SessionKey);

            _eventService.Publish(new PlayDialogueEvent(_dialogueSequence, _priority));
        }
    }
}
