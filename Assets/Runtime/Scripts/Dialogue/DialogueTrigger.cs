using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public class DialogueTrigger : MonoBehaviour
    {
        [SerializeField] protected SO_DialogueSequence _sequence;
        [SerializeField] protected Transform _speaker;
        [SerializeField] protected string _triggerId;
        [SerializeField] protected bool _playOnce = true;

        private IDialogueService _dialogue;
        private IPlayerPersistenceService _persistence;

        [Inject]
        private void Inject(IDialogueService dialogue, IPlayerPersistenceService persistence)
        {
            _dialogue = dialogue;
            _persistence = persistence;
        }

        public void TryTriggerDialogue()
        {
            if (_dialogue == null || _sequence == null || _speaker == null)
                return;

            if (_playOnce && !string.IsNullOrEmpty(_triggerId) && _persistence != null && _persistence.HasKey(_triggerId))
                return;

            _dialogue.PlayDialogue(_sequence, _speaker);

            if (_playOnce && !string.IsNullOrEmpty(_triggerId))
                _persistence?.AddKey(_triggerId);
        }
    }
}
