using UnityEngine;
namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "SO_DialogueSequence", menuName = "Scriptable Objects/SO_DialogueSequence")]
    public class SO_DialogueSequence : ScriptableObject
    {
        [SerializeField] private SO_DialogueLine _startingLine;
        [SerializeField] private DialogueType _dialogueType;
        public SO_DialogueLine StartingLine => _startingLine;
        public DialogueType Type => _dialogueType;
    }
    public enum DialogueType
    {
        Parallel,
        Interrupting
    }
}
