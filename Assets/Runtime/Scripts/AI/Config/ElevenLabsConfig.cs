using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "ElevenLabsConfig", menuName = "Dumyat/AI/ElevenLabs Config")]
    public class ElevenLabsConfig : ScriptableObject
    {
        // Effective defaults, also used as the fallback when a field is left blank (e.g. on an
        // existing asset created before these fields were added — Unity deserializes those with
        // empty strings, so the getters below fall back to these instead of sending an empty model).
        public const string DefaultTtsModelId = "eleven_multilingual_v2";  // v2 multilingual — required for Arabic
        public const string DefaultTtsModelIdV3 = "eleven_v3";             // v3 — most expressive
        public const string DefaultSttModelId = "scribe_v2_realtime";      // Scribe realtime

        [Header("Authentication")]
        [Tooltip("ElevenLabs API key used to authenticate text-to-speech / speech-to-text requests.")]
        [SerializeField] private string _apiKey;

        [Header("Voice")]
        [Tooltip("ElevenLabs voice ID used for speech synthesis.")]
        [SerializeField] private string _voiceId;

        [Header("Models")]
        [Tooltip("Eleven v2 text-to-speech model ID. eleven_multilingual_v2 = higher quality (required " +
                 "for Arabic); eleven_flash_v2_5 = lower latency. Blank falls back to the default.")]
        [SerializeField] private string _ttsModelId = DefaultTtsModelId;

        [Tooltip("Eleven v3 text-to-speech model ID (used when a voice line targets v3). " +
                 "Blank falls back to the default.")]
        [SerializeField] private string _ttsModelIdV3 = DefaultTtsModelIdV3;

        [Tooltip("Speech-to-text (Scribe realtime) model ID. Blank falls back to the default.")]
        [SerializeField] private string _sttModelId = DefaultSttModelId;

        public string ApiKey => _apiKey;
        public string VoiceId => _voiceId;

        /// <summary>Configured Eleven v2 TTS model ID, or <see cref="DefaultTtsModelId"/> when left blank.</summary>
        public string TtsModelId => string.IsNullOrWhiteSpace(_ttsModelId) ? DefaultTtsModelId : _ttsModelId;

        /// <summary>Configured Eleven v3 TTS model ID, or <see cref="DefaultTtsModelIdV3"/> when left blank.</summary>
        public string TtsModelIdV3 => string.IsNullOrWhiteSpace(_ttsModelIdV3) ? DefaultTtsModelIdV3 : _ttsModelIdV3;

        /// <summary>Configured STT model ID, or <see cref="DefaultSttModelId"/> when left blank.</summary>
        public string SttModelId => string.IsNullOrWhiteSpace(_sttModelId) ? DefaultSttModelId : _sttModelId;
    }
}
