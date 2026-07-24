using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace DumyatRuntime
{
    /// <summary>
    /// Singleton client that converts text to speech using the ElevenLabs REST API.
    /// The model ID defaults to the one configured on <see cref="ElevenLabsConfig"/>
    /// (<c>eleven_multilingual_v2</c> out of the box — required for Arabic), but callers may pass an
    /// explicit model. The resulting <see cref="AudioClip"/> is delivered through a callback. Errors
    /// (bad key, quota exceeded, bad voice ID) are logged with the HTTP status code and forwarded to
    /// the failure callback.
    /// </summary>
    public class ElevenLabsClient : MonoBehaviour
    {
        private const string EndpointFormat = "https://api.elevenlabs.io/v1/text-to-speech/{0}";
        private const string DefaultConfigResourcePath = "AI/Secrets/ElevenLabsConfig";

        [Header("Configuration")]
        [Tooltip("ElevenLabs credentials/voice. If left empty it is loaded from Resources/AI/Secrets/ElevenLabsConfig.")]
        [SerializeField] private ElevenLabsConfig _config;

        // ElevenLabs REJECTS a speed outside this range with HTTP 400 (invalid_voice_settings),
        // which silently kills TTS. Keep the slider AND the runtime clamp pinned to it.
        private const float MinSpeed = 0.7f;
        private const float MaxSpeed = 1.2f;

        [Header("Model Version")]
        [Tooltip("DEFAULT ElevenLabs model generation, used when a caller supplies no per-line settings. " +
                 "v2 = speed/stability/similarity/style; v3 = stability + language override (more expressive). " +
                 "A per-merchant MerchantConfig overrides this per line.")]
        [SerializeField] private ElevenLabsModelVersion _modelVersion = ElevenLabsModelVersion.V2;

        [Header("Voice Settings — Eleven v2")]
        [Tooltip("Speaking speed. 1 = normal, < 1 = slower, > 1 = faster. ElevenLabs only accepts " +
                 "0.7–1.2; values outside this are rejected (HTTP 400), so the range is enforced here.")]
        [SerializeField, Range(MinSpeed, MaxSpeed)] private float _speed = 1f;
        [Tooltip("Stability. Lower = more variable/expressive, higher = more stable/consistent.")]
        [SerializeField, Range(0f, 1f)] private float _stability = 0.5f;
        [Tooltip("Similarity boost. How closely the output matches the original voice.")]
        [SerializeField, Range(0f, 1f)] private float _similarityBoost = 0.75f;
        [Tooltip("Style exaggeration. 0 = neutral, 1 = highly expressive. Over ~0.5 may add instability. " +
                 "Only affects v2 models.")]
        [SerializeField, Range(0f, 1f)] private float _style = 0f;
        [Tooltip("Language override (ISO 639-1 code, e.g. 'ar' for Arabic, 'en' for English). Forces the " +
                 "spoken language instead of letting the model auto-detect from the text. Leave BLANK " +
                 "to auto-detect. Only some models honour it; an empty value is omitted from the request.")]
        [SerializeField] private string _languageOverride = "";
        [Tooltip("Speaker boost. Boosts similarity to the original speaker (slightly higher latency).")]
        [SerializeField] private bool _useSpeakerBoost = true;

        [Header("Voice Settings — Eleven v3")]
        [Tooltip("Stability for Eleven v3. v3 accepts only three settings — 0 = Creative (most " +
                 "expressive/variable), 0.5 = Natural, 1 = Robust (most stable). Authored values are " +
                 "snapped to the nearest of these.")]
        [SerializeField, Range(0f, 1f)] private float _v3Stability = 0.5f;
        [Tooltip("Language override for Eleven v3 (ISO 639-1, e.g. 'ar' for Arabic). Leave BLANK to " +
                 "auto-detect; an empty value is omitted from the request.")]
        [SerializeField] private string _v3LanguageOverride = "";

        [Header("Reliability")]
        [Tooltip("Seconds to wait for the audio response before treating the request as failed.")]
        [SerializeField, Min(1)] private int _timeoutSeconds = 30;
        [Tooltip("Message forwarded to the failure callback when TTS cannot be completed.")]
        [SerializeField, TextArea] private string _fallbackMessage = "Text-to-speech is unavailable right now.";

        /// <summary>True when a config with non-empty API key and voice ID is available.</summary>
        public bool IsConfigured => _config != null
            && !string.IsNullOrWhiteSpace(_config.ApiKey)
            && !string.IsNullOrWhiteSpace(_config.VoiceId);

        /// <summary>True when an API key is present. Used by per-voice callers (e.g. the TTS model)
        /// that supply their own voice ID and so don't require a configured default voice.</summary>
        public bool HasApiKey => _config != null && !string.IsNullOrWhiteSpace(_config.ApiKey);

        private void Awake()
        {
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
        /// Synthesises <paramref name="text"/> using the configured voice and default model.
        /// On success <paramref name="onSuccess"/> receives a ready-to-play <see cref="AudioClip"/>;
        /// on failure <paramref name="onFailure"/> receives a human-readable message.
        /// </summary>
        public void Speak(string text, Action<AudioClip> onSuccess, Action<string> onFailure = null)
        {
            if (!IsConfigured)
            {
                Debug.LogWarning("ElevenLabsClient: Missing API key/voice config; cannot synthesise speech.");
                onFailure?.Invoke(_fallbackMessage);
                return;
            }

            // Empty model → resolved from the ElevenLabsConfig SO in the overload below. Null settings →
            // the overload uses this client's own serialized voice parameters.
            Speak(text, _config.VoiceId, null, null, onSuccess, onFailure);
        }

        /// <summary>
        /// Synthesises <paramref name="text"/> with an explicit <paramref name="voiceId"/> and
        /// <paramref name="modelId"/> (e.g. a per-merchant Egyptian-Arabic voice, or a low-latency
        /// flash model). Falls back to the default model when <paramref name="modelId"/> is empty.
        /// <paramref name="voiceSettings"/> optionally overrides this client's serialized voice
        /// parameters (speed/stability/similarity/style) for this call only — pass a per-merchant
        /// <see cref="TtsVoiceSettings"/>, or null to use the client's own fields.
        /// Used by <see cref="ElevenLabsTTSModel"/> so a single REST/auth/error path is shared.
        /// </summary>
        public void Speak(string text, string voiceId, string modelId,
            TtsVoiceSettings? voiceSettings,
            Action<AudioClip> onSuccess, Action<string> onFailure = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogWarning("ElevenLabsClient: Ignoring empty text.");
                onFailure?.Invoke(_fallbackMessage);
                return;
            }

            if (_config == null || string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                Debug.LogWarning("ElevenLabsClient: Missing API key; cannot synthesise speech.");
                onFailure?.Invoke(_fallbackMessage);
                return;
            }

            if (string.IsNullOrWhiteSpace(voiceId))
            {
                Debug.LogWarning("ElevenLabsClient: No voice ID supplied; cannot synthesise speech.");
                onFailure?.Invoke(_fallbackMessage);
                return;
            }

            // The per-line settings pick the model version (else this client's default); the version
            // then resolves the actual model ID (v3 ignores the v2-only per-pool model override).
            ElevenLabsModelVersion version = voiceSettings?.Version ?? _modelVersion;
            string model = ResolveModel(version, modelId);
            StartCoroutine(SpeakRoutine(text, voiceId, model, version, voiceSettings, onSuccess, onFailure));
        }

        // The default v2 TTS model comes from the ElevenLabsConfig SO (or its built-in default when no
        // config is assigned), so designers can change it without touching code.
        private string ResolveDefaultModel() =>
            _config != null ? _config.TtsModelId : ElevenLabsConfig.DefaultTtsModelId;

        // Resolves the model ID for a given version. v3 always uses the config's v3 model (the per-pool
        // modelId override is a v2 model and is ignored); v2 honours an explicit per-pool model first.
        private string ResolveModel(ElevenLabsModelVersion version, string explicitModelId)
        {
            if (version == ElevenLabsModelVersion.V3)
                return _config != null ? _config.TtsModelIdV3 : ElevenLabsConfig.DefaultTtsModelIdV3;

            return string.IsNullOrWhiteSpace(explicitModelId) ? ResolveDefaultModel() : explicitModelId;
        }

        /// <summary>
        /// The TTS model ID a synthesis call will use when no explicit override is passed — resolved
        /// from this client's default <see cref="ElevenLabsModelVersion"/> and the ElevenLabsConfig SO
        /// (or the built-in default). Exposed so pool entries (<see cref="ElevenLabsTTSModel"/>) can
        /// report the model they will EFFECTIVELY use.
        /// </summary>
        public string EffectiveTtsModelId => ResolveModel(_modelVersion, null);

        private IEnumerator SpeakRoutine(string text, string voiceId, string modelId,
            ElevenLabsModelVersion version, TtsVoiceSettings? voiceSettings,
            Action<AudioClip> onSuccess, Action<string> onFailure)
        {
            // Per-call settings (e.g. a per-merchant TtsVoiceSettings) win; otherwise use this client's
            // serialized fields for the resolved version. Each builder clamps to ElevenLabs' bounds.
            string json = version == ElevenLabsModelVersion.V3
                ? BuildRequestJsonV3(text, modelId,
                    voiceSettings?.Stability ?? _v3Stability,
                    voiceSettings?.LanguageOverride ?? _v3LanguageOverride)
                : BuildRequestJsonV2(text, modelId,
                    voiceSettings?.Speed ?? _speed,
                    voiceSettings?.Stability ?? _stability,
                    voiceSettings?.SimilarityBoost ?? _similarityBoost,
                    voiceSettings?.Style ?? _style);

            string url = string.Format(EndpointFormat, voiceId);
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
            Debug.Log($"ElevenLabsClient: POST {url} — {version}, model={modelId} ({body.Length} bytes)");

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

        private string BuildRequestJsonV2(string text, string modelId,
            float speed, float stability, float similarityBoost, float style)
        {
            // Built by hand rather than via JsonUtility on purpose: JsonUtility widens `float` fields
            // to double when serializing, so a boundary speed like 1.2f is emitted as
            // "1.2000000476837158" — just OVER ElevenLabs' 1.2 max — and the whole request is rejected
            // (400 invalid_voice_settings). Formatting the numbers ourselves at fixed precision keeps
            // them exactly within the accepted bounds. It also lets us omit language_code cleanly when
            // no override is set (ElevenLabs rejects an empty language_code).
            string language = string.IsNullOrWhiteSpace(_languageOverride) ? null : _languageOverride.Trim();

            var sb = new System.Text.StringBuilder(256);
            sb.Append('{');
            sb.Append("\"text\":\"").Append(JsonEscape(text)).Append('"');
            sb.Append(",\"model_id\":\"").Append(JsonEscape(modelId)).Append('"');
            if (language != null)
                sb.Append(",\"language_code\":\"").Append(JsonEscape(language)).Append('"');
            sb.Append(",\"voice_settings\":{")
              .Append("\"stability\":").Append(Num(Mathf.Clamp01(stability)))
              .Append(",\"similarity_boost\":").Append(Num(Mathf.Clamp01(similarityBoost)))
              .Append(",\"style\":").Append(Num(Mathf.Clamp01(style)))
              .Append(",\"speed\":").Append(Num(Mathf.Clamp(speed, MinSpeed, MaxSpeed)))
              .Append(",\"use_speaker_boost\":").Append(_useSpeakerBoost ? "true" : "false")
              .Append('}');
            sb.Append('}');
            return sb.ToString();
        }

        // Eleven v3 request body. v3 takes a DIFFERENT, smaller parameter set than v2: stability plus
        // an optional language override. speed / similarity_boost / style are v2-only and omitted here.
        private string BuildRequestJsonV3(string text, string modelId, float stability, string languageOverride)
        {
            // v3 exposes stability as three discrete settings (0 Creative / 0.5 Natural / 1 Robust);
            // snap an authored in-between value so the API can't reject it (mirrors the v2 speed clamp).
            float snapped = SnapV3Stability(stability);
            string language = string.IsNullOrWhiteSpace(languageOverride) ? null : languageOverride.Trim();

            var sb = new System.Text.StringBuilder(256);
            sb.Append('{');
            sb.Append("\"text\":\"").Append(JsonEscape(text)).Append('"');
            sb.Append(",\"model_id\":\"").Append(JsonEscape(modelId)).Append('"');
            if (language != null)
                sb.Append(",\"language_code\":\"").Append(JsonEscape(language)).Append('"');
            sb.Append(",\"voice_settings\":{")
              .Append("\"stability\":").Append(Num(snapped))
              .Append('}');
            sb.Append('}');
            return sb.ToString();
        }

        // v3 stability is one of {0, 0.5, 1}; snap to the nearest so any authored value stays valid.
        private static float SnapV3Stability(float v)
        {
            v = Mathf.Clamp01(v);
            if (v < 0.25f) return 0f;
            if (v < 0.75f) return 0.5f;
            return 1f;
        }

        // Fixed-precision, culture-invariant number (e.g. "1.2", "0.51") so voice-settings values land
        // exactly on ElevenLabs' accepted bounds instead of being widened to a longer decimal.
        private static string Num(float v) =>
            v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        // Minimal JSON string escaper for the text/model/language fields — the LLM's Arabic line may
        // contain quotes, backslashes or control characters. Arabic letters pass through as UTF-8.
        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static void LogHttpError(UnityWebRequest request)
        {
            long code = request.responseCode;
            string hint = code switch
            {
                400 => "bad request — invalid model_id, invalid voice_settings (e.g. speed must be 0.7–1.2), or malformed body",
                401 => "invalid or missing API key",
                402 => "payment required — account out of credits or subscription inactive",
                422 => "invalid voice ID or malformed request body",
                429 => "quota exceeded or rate-limited",
                _   => request.error
            };

            // ElevenLabs returns a JSON error body ("model_not_found", "invalid_voice_settings", …)
            // that pinpoints the cause. The success handler is a DownloadHandlerAudioClip, whose
            // text/data accessors throw when the payload isn't audio, so read it defensively.
            string body = TryReadBody(request);
            string detail = string.IsNullOrEmpty(body) ? "" : $" | body: {body}";
            Debug.LogWarning($"ElevenLabsClient: HTTP {code} — {hint}{detail}");
        }

        private static string TryReadBody(UnityWebRequest request)
        {
            try
            {
                byte[] raw = request.downloadHandler?.data;
                if (raw != null && raw.Length > 0)
                    return System.Text.Encoding.UTF8.GetString(raw);
            }
            catch
            {
                // DownloadHandlerAudioClip refuses to expose a non-audio error payload; ignore.
            }
            return null;
        }
    }
}
