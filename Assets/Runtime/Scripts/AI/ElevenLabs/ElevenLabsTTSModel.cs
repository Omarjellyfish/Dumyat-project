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

        public string ModelName => $"ElevenLabs TTS ({EffectiveModelId})";

        // The model actually used at synthesis time: the override when set, otherwise the shared
        // ElevenLabsConfig's model (mirrors ElevenLabsClient's own resolution). Keeps ModelName
        // truthful when the override field is left blank.
        private string EffectiveModelId
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_modelId)) return _modelId;
                return ElevenLabsClient.Singleton != null
                    ? ElevenLabsClient.Singleton.EffectiveTtsModelId
                    : ElevenLabsConfig.DefaultTtsModelId;
            }
        }

        // The REST client validates the API key per request and routes failures to onError, so
        // "available" just means the client exists and has a key configured.
        public bool IsAvailable => ElevenLabsClient.Singleton != null && ElevenLabsClient.Singleton.HasApiKey;

        public int Priority => _priority;

        public void Synthesize(string text, string voiceId, Action<AudioClip> onSuccess, Action<string> onError)
        {
            if (ElevenLabsClient.Singleton == null)
            {
                onError?.Invoke("ElevenLabsClient singleton is missing.");
                return;
            }

            ElevenLabsClient.Singleton.Speak(text, voiceId, _modelId,
                onSuccess: clip => onSuccess?.Invoke(clip),
                onFailure: msg => onError?.Invoke(msg));
        }
    }
}
