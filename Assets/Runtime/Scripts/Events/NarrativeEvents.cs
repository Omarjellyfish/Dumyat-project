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
        public readonly string CharacterId;
        public readonly string SpeakerName;
        public readonly string SubtitleText;
        public readonly AudioClip VoiceClip;
        public readonly float Duration;

        public DialogueLineStartedEvent(string characterId, string speakerName, string subtitleText, AudioClip voiceClip, float duration)
        {
            CharacterId = characterId;
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

    /// <summary>
    /// Published when a dialogue sequence finishes and nothing is left queued or stacked.
    ///
    /// <see cref="Sequence"/> names the last sequence actually played. It exists because "some dialogue
    /// ended" is not enough to act on: the father waits for his own opening speech before he sets off,
    /// and without an identity here a fish remark ending on the other side of the beach released him
    /// mid-sentence. Subscribers that care WHICH line ended should compare against it; the rest can
    /// keep ignoring it. May be null if nothing had played.
    /// </summary>
    public readonly struct DialogueEndedEvent : IGameEvent
    {
        public readonly DialogueSequenceSO Sequence;

        public DialogueEndedEvent(DialogueSequenceSO sequence)
        {
            Sequence = sequence;
        }
    }

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
