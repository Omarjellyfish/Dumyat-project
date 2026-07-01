using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "SO_DialogueSequence", menuName = "Scriptable Objects/SO_DialogueSequence")]
    public class SO_DialogueSequence : ScriptableObject
    {
        [SerializeField] private List<SO_DialogueLine> _dialogueLines;
        [SerializeField] private DialogueType _dialogueType;

        public List<SO_DialogueLine> DialogueLines => _dialogueLines;
        public DialogueType Type => _dialogueType;
    }

    public enum DialogueType
    {
        Parallel,
        Interrupting
    }
}