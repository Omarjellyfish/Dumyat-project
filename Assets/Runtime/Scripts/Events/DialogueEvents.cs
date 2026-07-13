using System;
using System.Collections.Generic;

namespace DumyatRuntime
{
    /// <summary>Published when a dialogue sequence starts playing.</summary>
    public readonly struct DialogueStartedEvent : IGameEvent
    {
        public readonly SO_DialogueSequence Sequence;

        public DialogueStartedEvent(SO_DialogueSequence sequence)
        {
            Sequence = sequence;
        }
    }

    /// <summary>Published each time a new line's text/audio starts. Useful for subtitle UI, lipsync, etc.</summary>
    public readonly struct DialogueLineShownEvent : IGameEvent
    {
        public readonly SO_DialogueLine Line;
        public readonly string DisplayText;

        public DialogueLineShownEvent(SO_DialogueLine line, string displayText)
        {
            Line = line;
            DisplayText = displayText;
        }
    }

    /// <summary>
    /// Published when DialogueManager reaches a branching line. Any Choice UI
    /// subscribes to this, shows buttons for each choice, and invokes SelectChoice
    /// with the one the player picked. DialogueManager waits until that happens.
    /// </summary>
    public readonly struct DialogueChoicesPresentedEvent : IGameEvent
    {
        public readonly IReadOnlyList<DialogueChoice> Choices;
        public readonly Action<DialogueChoice> SelectChoice;

        public DialogueChoicesPresentedEvent(IReadOnlyList<DialogueChoice> choices, Action<DialogueChoice> selectChoice)
        {
            Choices = choices;
            SelectChoice = selectChoice;
        }
    }

    /// <summary>Published when a dialogue sequence has fully finished (replaces the old static C# event).</summary>
    public readonly struct DialogueFinishedEvent : IGameEvent
    {
        public readonly SO_DialogueSequence Sequence;

        public DialogueFinishedEvent(SO_DialogueSequence sequence)
        {
            Sequence = sequence;
        }
    }
}
