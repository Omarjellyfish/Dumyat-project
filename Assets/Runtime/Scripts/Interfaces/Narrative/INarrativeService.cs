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

        /// <summary>
        /// Throw away EVERYTHING — the line playing, the interrupted stack beneath it and the waiting
        /// queue — without advancing to any of it.
        ///
        /// <see cref="CancelCurrent"/> is not this: it finishes the current line, which pulls the next
        /// one off the queue and starts it. That is right for "stop talking about this", and wrong for
        /// leaving the scene, where the whole conversation has to end at once. The engine is a root
        /// singleton and outlives the scene, so without this it keeps feeding the next town's speakers
        /// lines belonging to the one the player just left.
        /// </summary>
        void StopAll();
    }
}
