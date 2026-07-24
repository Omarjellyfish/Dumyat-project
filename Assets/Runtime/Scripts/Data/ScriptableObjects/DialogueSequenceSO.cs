using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    public enum DialoguePriority { Low = 0, Normal = 1, High = 2, Critical = 3 }

    /// <summary>
    /// A dialogue screenplay. References character SOs for audio, contains lines and choice points.
    /// Create via: Right-click → Create → Dumyat/Narrative/Dialogue Sequence.
    /// </summary>
    [CreateAssetMenu(fileName = "New Dialogue", menuName = "Dumyat/Narrative/Dialogue Sequence")]
    public class DialogueSequenceSO : ScriptableObject
    {
        [Tooltip("Unique ID for deduplication (no two instances of the same sequence in the stack)")]
        public string SequenceId;

        public DialoguePriority Priority = DialoguePriority.Normal;

        [Tooltip("If false, this dialogue cannot be interrupted regardless of incoming priority")]
        public bool CanBeInterrupted = true;

        [Tooltip("If true, resume this dialogue after an interruption finishes")]
        public bool ResumeAfterInterruption = true;

        public List<DialogueLine> Lines = new();
        public List<DialogueChoicePoint> ChoicePoints = new();
    }
}
