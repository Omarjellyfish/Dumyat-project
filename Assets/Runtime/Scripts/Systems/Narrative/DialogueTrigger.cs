using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Reusable trigger component. Place on an NPC or world object.
    /// Call TriggerDialogue() from your interaction system.
    /// </summary>
    public class DialogueTrigger : MonoBehaviour
    {
        [Header("Dialogue")]
        [SerializeField] private DialogueSequenceSO _sequence;
        [SerializeField] private DialoguePriority _priority = DialoguePriority.Normal;

        [Header("Conditions")]
        [SerializeField] private bool _isOneShot;
        [SerializeField] private string _oneShotKey;
        [SerializeField] private string _requiredPersistenceKey;

        [Inject] private readonly IEventService _eventService;
        [Inject] private readonly IPlayerPersistenceService _persistenceService;

        /// <summary>Call this from your interaction system when the player interacts.</summary>
        public void TriggerDialogue()
        {
            // Check required key
            if (!string.IsNullOrEmpty(_requiredPersistenceKey)
                && !_persistenceService.HasKey(_requiredPersistenceKey))
                return;

            // Check one-shot
            if (_isOneShot && !string.IsNullOrEmpty(_oneShotKey)
                && _persistenceService.HasKey(_oneShotKey))
                return;

            _eventService.Publish(new PlayDialogueEvent(_sequence, _priority));

            // Mark as played
            if (_isOneShot && !string.IsNullOrEmpty(_oneShotKey))
                _persistenceService.AddKey(_oneShotKey);
        }
    }
}
