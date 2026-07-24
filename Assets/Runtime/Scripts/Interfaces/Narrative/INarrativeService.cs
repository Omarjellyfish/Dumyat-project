namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for the stack-based narrative engine.
    /// Injected into MonoBehaviours that need to interact with active dialogue.
    /// </summary>
    public interface INarrativeService
    {
        /// <summary>Whether a dialogue is currently playing.</summary>
        bool IsPlaying { get; }

        /// <summary>Play a dialogue sequence. Handles interruption/queueing internally.</summary>
        void PlayDialogue(DialogueSequenceSO sequence, DialoguePriority priority);

        /// <summary>Advance to the next line. Called by UI or timer.</summary>
        void AdvanceLine();

        /// <summary>Select a choice by index. Called by UI.</summary>
        void SelectChoice(int choiceIndex);

        /// <summary>Cancel and discard the currently playing dialogue.</summary>
        void CancelCurrent();
    }
}
