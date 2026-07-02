using System;
using UnityEngine;
using System.Collections.Generic;
namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "NewDialogueLine", menuName = "Dialogue/Dialogue Line")]
    public class SO_DialogueLine : ScriptableObject
    {
        [SerializeField] private string _arabicText;
        [SerializeField] private string _englishText;
        [SerializeField] private AudioClip _voiceClip;
        [SerializeField] private string _speakerName;
        [SerializeField] private SO_DialogueLine _nextLine; // used when NO choices exist
        [SerializeField] private List<DialogueChoice> _choices;

        public string ArabicText => _arabicText;
        public string EnglishText => _englishText;
        public AudioClip VoiceClip => _voiceClip;
        public string SpeakerName => _speakerName;
        public SO_DialogueLine NextLine => _nextLine;
        public List<DialogueChoice> Choices => _choices;
        public bool HasChoices => _choices != null && _choices.Count > 0;
    }
}
