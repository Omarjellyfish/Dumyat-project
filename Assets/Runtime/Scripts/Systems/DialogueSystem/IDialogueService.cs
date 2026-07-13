using UnityEngine;

namespace DumyatRuntime
{
    public interface IDialogueService
    {
        bool IsPlaying { get; }

        void PlayDialogue(SO_DialogueSequence sequence, Transform speaker);

        // Called by Choice UI in response to a DialogueChoicesPresentedEvent
        // to tell the manager which branch to continue on.
        void SelectChoice(DialogueChoice choice);
    }
}
