using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    public class NarrativeTriggerZone : MonoBehaviour
    {
        [SerializeField] private DialogueSequenceSO _dialogueSequence;
        [SerializeField] private DialoguePriority _priority = DialoguePriority.Normal;
        [SerializeField] private bool _triggerOnce = true;

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

        private void OnTriggerEnter(Collider other)
        {
            VerboseLog.Trace($"[NarrativeTriggerZone] OnTriggerEnter entered by {other.name} (Tag: {other.tag})");
            
            if (!other.CompareTag("Player")) return;
            
            VerboseLog.Trace($"[NarrativeTriggerZone] Player detected. _hasFired: {_hasFired}");
            if (_triggerOnce && _hasFired) return;

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
            
            _eventService.Publish(new PlayDialogueEvent(_dialogueSequence, _priority));
        }
    }
}
