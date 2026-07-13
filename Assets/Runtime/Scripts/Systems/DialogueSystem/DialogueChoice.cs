using System;
using UnityEngine;

namespace DumyatRuntime
{
    // Not a ScriptableObject on purpose - this only ever exists inside a
    // SO_DialogueLine's Choices list, never as its own asset.
    [Serializable]
    public class DialogueChoice
    {
        [SerializeField] private string _arabicDisplayText;
        [SerializeField] private string _englishDisplayText;
        [SerializeField] private SO_DialogueLine _nextLine;

        public string ArabicDisplayText => _arabicDisplayText;
        public string EnglishDisplayText => _englishDisplayText;
        public SO_DialogueLine NextLine => _nextLine;
    }
}
