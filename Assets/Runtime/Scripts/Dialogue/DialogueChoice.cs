    using UnityEngine;

    namespace DumyatRuntime
    {
        [System.Serializable]
        public class DialogueChoice
        {
            [SerializeField] string _choiceText;
            [SerializeField] private SO_DialogueLine _nextLine;

            public string ChoiceText => _choiceText;
            public SO_DialogueLine NextLine => _nextLine;

        }
    }
