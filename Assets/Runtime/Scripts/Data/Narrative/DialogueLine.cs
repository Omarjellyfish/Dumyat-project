using System;
using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    [Serializable]
    public class DialogueLine
    {
        [Tooltip("Who speaks this line")]
        public CharacterDialogueDataSO Character;

        [Tooltip("Key into the character's VoiceLines list")]
        public string VoiceLineKey;

        [TextArea(2, 4)]
        [Tooltip("Subtitle text displayed to the player")]
        public string SubtitleText;

        [Header("Persistence")]
        [Tooltip("If set, this line only plays when this key matches RequiredState")]
        public string PersistenceConditionKey;

        [Tooltip("true = key must exist, false = key must NOT exist")]
        public bool RequiredState = true;

        [Tooltip("If set, this persistence key is added when this line plays")]
        public string PersistenceKeyToSet;

        [Header("Flow")]
        [Tooltip("-1 = continue to next line in list. Otherwise jump to this index.")]
        public int NextLineIndex = -1;
    }

    [Serializable]
    public class DialogueChoicePoint
    {
        [Tooltip("This choice appears after the line at this index finishes")]
        public int AfterLineIndex;

        [TextArea(1, 2)]
        public string PromptText;

        [Tooltip("0 = wait forever")]
        public float TimeoutSeconds;

        [Tooltip("Line index to jump to on timeout. -1 = pick first choice.")]
        public int TimeoutJumpIndex = -1;

        public List<DialogueChoice> Choices = new();
    }

    [Serializable]
    public class DialogueChoice
    {
        public string ChoiceText;

        [Tooltip("Continue from this line index after choosing")]
        public int JumpToLineIndex;

        [Header("Persistence Condition (optional — hides choice if not met)")]
        public string RequiredPersistenceKey;
        public bool RequiredState = true;
    }
}
