using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    public abstract class DialogueTrigger : MonoBehaviour
    {
        [SerializeField] protected SO_DialogueSequence _sequence;
        [SerializeField] protected Transform _speaker;
        [SerializeField] protected bool _playOnce = true;

        protected IDialogueService _dialogueService;
        protected bool _hasPlayed;

        [Inject]
        private void Inject(IDialogueService dialogueService)
        {
            _dialogueService = dialogueService;
        }

        protected void TryStartDialogue()
        {
            if (_playOnce && _hasPlayed) return;
            if (_sequence == null || _dialogueService == null) return;

            Transform speakerTransform = _speaker != null ? _speaker : transform;
            _dialogueService.PlayDialogue(_sequence, speakerTransform);
            _hasPlayed = true;
        }

        // Lets an external system (save/reset, replay-for-testing, etc.)
        // allow this trigger to fire again.
        public void ResetTrigger()
        {
            _hasPlayed = false;
        }
    }
}
