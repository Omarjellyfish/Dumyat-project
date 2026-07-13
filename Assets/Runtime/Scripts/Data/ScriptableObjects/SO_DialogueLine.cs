using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "SO_DialogueLine", menuName = "Scriptable Objects/SO_DialogueLine")]
    public class SO_DialogueLine : ScriptableObject
    {
        [Header("Speaker")]
        [SerializeField] private string _speakerName;

        [Header("Text")]
        [SerializeField, TextArea(2, 5)] private string _arabicText;
        [SerializeField, TextArea(2, 5)] private string _englishText;

        [Header("Audio")]
        [SerializeField] private AudioClip _voiceClip;

        [Header("Flow")]
        [Tooltip("Used when this line does NOT branch. Ignored if Choices has entries.")]
        [SerializeField] private SO_DialogueLine _nextLine;

        [Tooltip("If this has entries, the player must pick one instead of auto-continuing to Next Line.")]
        [SerializeField] private List<DialogueChoice> _choices = new List<DialogueChoice>();

        public string SpeakerName => _speakerName;
        public string ArabicText => _arabicText;
        public string EnglishText => _englishText;
        public AudioClip VoiceClip => _voiceClip;
        public SO_DialogueLine NextLine => _nextLine;
        public List<DialogueChoice> Choices => _choices;
        public bool HasChoices => _choices != null && _choices.Count > 0;
    }
}
