using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    public class CarpenteryTriggerZone : MonoBehaviour
    {
        [SerializeField] private DialogueSequenceSO _dialogueSequence;
        [SerializeField] private DialoguePriority _priority = DialoguePriority.Normal;
        [SerializeField] private bool _triggerOnce = true;

        [Inject] private readonly IEventService _eventService;

        private bool _hasFired;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            if (_triggerOnce && _hasFired) return;
            if (_dialogueSequence == null)
            {
                Debug.LogWarning($"[{name}] No DialogueSequenceSO assigned.", this);
                return;
            }

            _hasFired = true;
            _eventService.Publish(new PlayDialogueEvent(_dialogueSequence, _priority));
        }
    }
}