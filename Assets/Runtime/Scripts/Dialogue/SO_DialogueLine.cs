using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "NewDialogueLine", menuName = "Dialogue/Dialogue Line")]
    public class SO_DialogueLine : ScriptableObject
    {
        [SerializeField] private string _arabicText;
        [SerializeField] private string _englishText;
        [SerializeField] private AudioClip _voiceClip;
        [SerializeField] private string _speakerName;

        public string ArabicText => _arabicText;
        public string EnglishText => _englishText;
        public AudioClip VoiceClip => _voiceClip;
        public string SpeakerName => _speakerName;
    }
}
