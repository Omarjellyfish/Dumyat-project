using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>Published by any system to request a dialogue to play.</summary>
    public readonly struct PlayDialogueEvent : IGameEvent
    {
        public readonly DialogueSequenceSO Sequence;
        public readonly DialoguePriority Priority;

        public PlayDialogueEvent(DialogueSequenceSO sequence, DialoguePriority priority = DialoguePriority.Normal)
        {
            Sequence = sequence;
            Priority = priority;
        }
    }

    /// <summary>Published by NarrativeService when a new line starts playing.</summary>
    public readonly struct DialogueLineStartedEvent : IGameEvent
    {
        public readonly string SpeakerName;
        public readonly string SubtitleText;
        public readonly AudioClip VoiceClip;
        public readonly float Duration;

        public DialogueLineStartedEvent(string speakerName, string subtitleText, AudioClip voiceClip, float duration)
        {
            SpeakerName = speakerName;
            SubtitleText = subtitleText;
            VoiceClip = voiceClip;
            Duration = duration;
        }
    }

    /// <summary>Published when a choice point is reached.</summary>
    public readonly struct DialogueChoicesEvent : IGameEvent
    {
        public readonly string PromptText;
        public readonly IReadOnlyList<DialogueChoice> Choices;
        public readonly float TimeoutSeconds;

        public DialogueChoicesEvent(string promptText, IReadOnlyList<DialogueChoice> choices, float timeoutSeconds)
        {
            PromptText = promptText;
            Choices = choices;
            TimeoutSeconds = timeoutSeconds;
        }
    }

    /// <summary>Published when the player selects a choice (for UI to dismiss).</summary>
    public readonly struct DialogueChoiceDismissedEvent : IGameEvent { }

    /// <summary>Published when a dialogue sequence finishes or when all queued dialogues are done.</summary>
    public readonly struct DialogueEndedEvent : IGameEvent { }

    /// <summary>Published when active dialogue is interrupted by a higher-priority one.</summary>
    public readonly struct DialogueInterruptedEvent : IGameEvent { }

    /// <summary>Published when a previously interrupted dialogue resumes.</summary>
    public readonly struct DialogueResumedEvent : IGameEvent
    {
        public readonly string SpeakerName;

        public DialogueResumedEvent(string speakerName)
        {
            SpeakerName = speakerName;
        }
    }
}
