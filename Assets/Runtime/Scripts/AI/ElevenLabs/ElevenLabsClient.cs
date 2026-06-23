using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace DumyatRuntime
{
    /// <summary>
    /// Singleton client that converts text to speech using the ElevenLabs REST API.
    /// Uses <c>eleven_multilingual_v2</c> (required for Arabic) and delivers the resulting
    /// <see cref="AudioClip"/> through a callback. Errors (bad key, quota exceeded, bad voice ID)
    /// are logged with the HTTP status code and forwarded to the failure callback.
    /// </summary>
    public class ElevenLabsClient : MonoBehaviour, IManager
    {
        public static ElevenLabsClient Singleton { get; private set; }

        private const string EndpointFormat = "https://api.elevenlabs.io/v1/text-to-speech/{0}";
        private const string DefaultConfigResourcePath = "AI/Secrets/ElevenLabsConfig";
        private const string ModelId = "eleven_multilingual_v2";

        [Header("Configuration")]
        [Tooltip("ElevenLabs credentials/voice. If left empty it is loaded from Resources/AI/Secrets/ElevenLabsConfig.")]
        [SerializeField] private ElevenLabsConfig _config;

        [Header("Voice")]
        [Tooltip("Speaking speed. 1 = normal, < 1 = slower, > 1 = faster. Clamped to ElevenLabs range 0.25–4.")]
        [SerializeField, Range(0.25f, 4f)] private float _speed = 1f;
        [Tooltip("Tone / style exaggeration. 0 = neutral, 1 = highly expressive. Only affects v2 models.")]
        [SerializeField, Range(0f, 1f)] private float _style = 0f;

        [Header("Reliability")]
        [Tooltip("Seconds to wait for the audio response before treating the request as failed.")]
        [SerializeField, Min(1)] private int _timeoutSeconds = 30;
        [Tooltip("Message forwarded to the failure callback when TTS cannot be completed.")]
        [SerializeField, TextArea] private string _fallbackMessage = "Text-to-speech is unavailable right now.";

        /// <summary>True when a config with non-empty API key and voice ID is available.</summary>
        public bool IsConfigured => _config != null
            && !string.IsNullOrWhiteSpace(_config.ApiKey)
            && !string.IsNullOrWhiteSpace(_config.VoiceId);

        private void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }
            Singleton = this;
            DontDestroyOnLoad(gameObject);

            if (_config == null)
                _config = Resources.Load<ElevenLabsConfig>(DefaultConfigResourcePath);

            if (_config == null)
            {
                Debug.LogWarning($"ElevenLabsClient: No ElevenLabsConfig assigned and none found at " +
                    $"Resources/{DefaultConfigResourcePath}. TTS requests will fail until one is provided.");
            }
            else if (!IsConfigured)
            {
                Debug.LogWarning("ElevenLabsClient: ElevenLabsConfig is missing an API key or voice ID.");
            }
        }

        /// <summary>
        /// Synthesises <paramref name="text"/> using the configured voice. On success
        /// <paramref name="onSuccess"/> receives a ready-to-play <see cref="AudioClip"/>;
        /// on failure <paramref name="onFailure"/> receives a human-readable message.
        /// </summary>
        public void Speak(string text, Action<AudioClip> onSuccess, Action<string> onFailure = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogWarning("ElevenLabsClient: Ignoring empty text.");
                onFailure?.Invoke(_fallbackMessage);
                return;
            }

            if (!IsConfigured)
            {
                Debug.LogWarning("ElevenLabsClient: Missing API key/voice config; cannot synthesise speech.");
                onFailure?.Invoke(_fallbackMessage);
                return;
            }

            StartCoroutine(SpeakRoutine(text, onSuccess, onFailure));
        }

        private IEnumerator SpeakRoutine(string text, Action<AudioClip> onSuccess, Action<string> onFailure)
        {
            string url = string.Format(EndpointFormat, _config.VoiceId);
            byte[] body = System.Text.Encoding.UTF8.GetBytes(BuildRequestJson(text));
            Debug.Log($"ElevenLabsClient: POST {url} — speed={_speed}, style={_style} ({body.Length} bytes)");

            using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "audio/mpeg");
                request.SetRequestHeader("xi-api-key", _config.ApiKey);
                request.timeout = _timeoutSeconds;

                yield return request.SendWebRequest();

                Debug.Log($"ElevenLabsClient: Response — result={request.result}, HTTP {request.responseCode}");

                if (request.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                    if (clip != null && clip.length > 0f)
                    {
                        onSuccess?.Invoke(clip);
                        yield break;
                    }

                    Debug.LogWarning("ElevenLabsClient: Request succeeded but returned an empty AudioClip.");
                }
                else
                {
                    LogHttpError(request);
                }
            }

            onFailure?.Invoke(_fallbackMessage);
        }

        private string BuildRequestJson(string text)
        {
            var payload = new ElevenLabsTtsRequest
            {
                text = text,
                model_id = ModelId,
                voice_settings = new ElevenLabsVoiceSettings
                {
                    stability = 0.5f,
                    similarity_boost = 0.75f,
                    style = _style,
                    speed = _speed,
                }
            };
            return JsonUtility.ToJson(payload);
        }

        private static void LogHttpError(UnityWebRequest request)
        {
            long code = request.responseCode;
            string hint = code switch
            {
                401 => "invalid or missing API key",
                402 => "payment required — account out of credits or subscription inactive",
                422 => "invalid voice ID or malformed request body",
                429 => "quota exceeded or rate-limited",
                _   => request.error
            };

            Debug.LogWarning($"ElevenLabsClient: HTTP {code} — {hint}");
        }
    }
}
