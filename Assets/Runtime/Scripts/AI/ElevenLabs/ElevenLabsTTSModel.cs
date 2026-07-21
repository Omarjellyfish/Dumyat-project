using System;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// ElevenLabs implementation of <see cref="ITTSModel"/> for the <see cref="TTSManager"/> pool.
    /// Pure synthesis: it delegates the REST/auth/error path to <see cref="ElevenLabsClient"/> and
    /// returns the resulting <see cref="AudioClip"/>. Playback is the manager's job.
    /// </summary>
    public class ElevenLabsTTSModel : MonoBehaviour, ITTSModel
    {
        // eleven_flash_v2_5 is preferred for LOW LATENCY in live, conversational haggling. Trade-off
        // vs eleven_multilingual_v2: flash responds faster and costs less but is slightly less
        // expressive. Both support Arabic. Switch to multilingual_v2 if voices sound flat in tests.
        // Leave BLANK to inherit the TTS model configured on the shared ElevenLabsConfig SO.
        [Header("Model")]
        [Tooltip("ElevenLabs model override for this pool entry. flash_v2_5 = low latency (preferred); " +
                 "multilingual_v2 = higher quality. Leave blank to inherit the ElevenLabsConfig SO's model.")]
        [SerializeField] private string _modelId = "eleven_flash_v2_5";

        [Tooltip("Selection priority within TTSManager; higher = preferred.")]
        [SerializeField] private int _priority = 0;

        [Tooltip("The REST client this model delegates synthesis to. Auto-resolved from this " +
                 "GameObject if left empty (the two components co-locate).")]
        [SerializeField] private ElevenLabsClient _client;

        private void Awake()
        {
            if (_client == null) _client = GetComponent<ElevenLabsClient>();
            if (_client == null)
                Debug.LogWarning("ElevenLabsTTSModel: no ElevenLabsClient assigned or found on this " +
                                 "GameObject; synthesis will fail until one is provided.");
        }

        public string ModelName => $"ElevenLabs TTS ({EffectiveModelId})";

        // The model actually used at synthesis time: the override when set, otherwise the shared
        // ElevenLabsConfig's model (mirrors ElevenLabsClient's own resolution). Keeps ModelName
        // truthful when the override field is left blank.
        private string EffectiveModelId
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_modelId)) return _modelId;
                return _client != null
                    ? _client.EffectiveTtsModelId
                    : ElevenLabsConfig.DefaultTtsModelId;
            }
        }

        // The REST client validates the API key per request and routes failures to onError, so
        // "available" just means the client exists and has a key configured.
        public bool IsAvailable => _client != null && _client.HasApiKey;

        public int Priority => _priority;

        public void Synthesize(string text, string voiceId, TtsVoiceSettings? voiceSettings,
            Action<AudioClip> onSuccess, Action<string> onError)
        {
            if (_client == null)
            {
                onError?.Invoke("ElevenLabsClient reference is missing.");
                return;
            }

            _client.Speak(text, voiceId, _modelId, voiceSettings,
                onSuccess: clip => onSuccess?.Invoke(clip),
                onFailure: msg => onError?.Invoke(msg));
        }
    }
}
