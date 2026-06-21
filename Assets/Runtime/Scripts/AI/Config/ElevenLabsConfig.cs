using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "ElevenLabsConfig", menuName = "Dumyat/AI/ElevenLabs Config")]
    public class ElevenLabsConfig : ScriptableObject
    {
        [Header("Authentication")]
        [Tooltip("ElevenLabs API key used to authenticate text-to-speech requests.")]
        [SerializeField] private string _apiKey;

        [Header("Voice")]
        [Tooltip("ElevenLabs voice ID used for speech synthesis.")]
        [SerializeField] private string _voiceId;

        public string ApiKey => _apiKey;
        public string VoiceId => _voiceId;
    }
}
