using UnityEngine;

namespace DumyatRuntime
{
    public class ZoneDialogueTrigger : DialogueTrigger
    {
        [SerializeField] private string _playerTag = "Player";

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag(_playerTag))
            {
                TryTriggerDialogue();
            }
        }
    }
}