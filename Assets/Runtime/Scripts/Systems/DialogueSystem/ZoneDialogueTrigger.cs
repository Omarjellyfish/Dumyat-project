using UnityEngine;

namespace DumyatRuntime
{
    // Attach to a GameObject with a trigger Collider.
    [RequireComponent(typeof(Collider))]
    public class ZoneDialogueTrigger : DialogueTrigger
    {
        [SerializeField] private string _playerTag = "Player";

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(_playerTag)) return;
            TryStartDialogue();
        }
    }
}
