using UnityEngine;

namespace DumyatRuntime
{
    public class DialogueTrigger : MonoBehaviour
    {
        [SerializeField] protected SO_DialogueSequence _sequence;
        [SerializeField] protected Transform _speaker;
        [SerializeField] protected string _triggerId;
        [SerializeField] protected bool _playOnce = true;

        public void TryTriggerDialogue()
        {
            if (_playOnce && PlayerPersistence.Singleton.HasKey(_triggerId))
                return;

            DialogueManager.Instance.PlayDialogue(_sequence, _speaker);

            if (_playOnce)
                PlayerPersistence.Singleton.AddKey(_triggerId);
        }
    }
}