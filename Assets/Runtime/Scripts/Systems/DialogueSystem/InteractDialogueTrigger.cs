using UnityEngine;

namespace DumyatRuntime
{
    public class InteractDialogueTrigger : DialogueTrigger
    {
        // Wire this method up to your interaction system's "on interact"
        // event (e.g. an XR Simple Interactable's Select Entered, or a
        // UnityEvent from a custom Interactable component).
        public void Interact()
        {
            TryStartDialogue();
        }
    }
}
