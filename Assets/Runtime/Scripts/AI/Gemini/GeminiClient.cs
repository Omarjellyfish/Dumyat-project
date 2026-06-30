using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace DumyatRuntime
{
    /// <summary>
    /// Singleton client that turns a text prompt into a Gemini-generated reply over Google's
    /// Generative Language REST API. It owns the request lifecycle: timeout, network/HTTP error
    /// handling and a basic retry, falling back to a configurable message when every attempt fails.
    /// Results are delivered through callbacks so callers stay decoupled (<see cref="ILlmClient"/>).
    /// </summary>
    public class GeminiClient : MonoBehaviour, IManager, ILlmClient, ILLMModel
    {
        public static GeminiClient Singleton { get; private set; }

        private const string EndpointFormat =
            "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";
        private const string DefaultConfigResourcePath = "AI/Secrets/GeminiConfig";

        [Header("Configuration")]
        [Tooltip("Gemini credentials/model. If left empty it is loaded from Resources/AI/Secrets/GeminiConfig.")]
        [SerializeField] private GeminiConfig _config;

        [Header("Model Pool")]
        [Tooltip("Selection priority within LLMManager; higher = preferred.")]
        [SerializeField] private int _priority = 0;

        [Header("Reliability")]
        [Tooltip("Seconds to wait for a response before the request is treated as a failure.")]
        [SerializeField, Min(1)] private int _timeoutSeconds = 20;
        [Tooltip("Extra attempts made after the first one fails (1 = one retry).")]
        [SerializeField, Min(0)] private int _maxRetries = 1;
        [Tooltip("Seconds to wait between a failed attempt and the next retry.")]
        [SerializeField, Min(0f)] private float _retryDelaySeconds = 0.5f;
        [Tooltip("Message handed to the failure callback once all attempts are exhausted.")]
        [SerializeField, TextArea] private string _fallbackMessage =
            "Sorry, I can't respond right now. Please try again in a moment.";

        /// <summary>True when a config with a non-empty API key is available.</summary>
        public bool IsConfigured => _config != null && !string.IsNullOrWhiteSpace(_config.ApiKey);

        // --- ILLMModel (pooled by LLMManager) --------------------------------------

        /// <inheritdoc/>
        public string ModelName => _config != null ? _config.ModelName : "Gemini";

        /// <inheritdoc/>
        public bool IsAvailable => IsConfigured;

        /// <inheritdoc/>
        public int Priority => _priority;

        /// <summary>
        /// Awaitable text generation for <see cref="LLMManager"/>: bridges the callback-based
        /// <see cref="SendPrompt"/> to a Task, yielding null on failure so the manager can fall back.
        /// </summary>
        public Task<string> GenerateTextAsync(string prompt)
        {
            var tcs = new TaskCompletionSource<string>();
            SendPrompt(prompt, reply => tcs.TrySetResult(reply), _ => tcs.TrySetResult(null));
            return tcs.Task;
        }

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
                _config = Resources.Load<GeminiConfig>(DefaultConfigResourcePath);

            if (_config == null)
            {
                Debug.LogWarning($"GeminiClient: No GeminiConfig assigned and none found at " +
                    $"Resources/{DefaultConfigResourcePath}. Requests will fail until one is provided.");
            }
            else if (string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                Debug.LogWarning("GeminiClient: GeminiConfig has an empty API key. Set it on the " +
                    "config asset before sending prompts.");
            }
        }

        /// <inheritdoc/>
        public void SendPrompt(string prompt, Action<string> onSuccess, Action<string> onFailure = null)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                Debug.LogWarning("GeminiClient: Ignoring empty prompt.");
                onFailure?.Invoke(_fallbackMessage);
                return;
            }

            if (!IsConfigured)
            {
                Debug.LogWarning("GeminiClient: Missing API key/config; cannot send prompt.");
                onFailure?.Invoke(_fallbackMessage);
                return;
            }

            StartCoroutine(SendPromptRoutine(prompt, onSuccess, onFailure));
        }

        private IEnumerator SendPromptRoutine(string prompt, Action<string> onSuccess, Action<string> onFailure)
        {
            string url = string.Format(EndpointFormat, _config.ModelName);
            byte[] body = Encoding.UTF8.GetBytes(BuildRequestJson(prompt));
            int totalAttempts = _maxRetries + 1;

            for (int attempt = 1; attempt <= totalAttempts; attempt++)
            {
                using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
                {
                    request.uploadHandler = new UploadHandlerRaw(body);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("x-goog-api-key", _config.ApiKey);
                    request.timeout = _timeoutSeconds; // counts connection errors AND timeouts as failures

                    yield return request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        if (TryParseReply(request.downloadHandler.text, out string reply))
                        {
                            onSuccess?.Invoke(reply);
                            yield break;
                        }

                        Debug.LogWarning($"GeminiClient: Attempt {attempt}/{totalAttempts} succeeded " +
                            "but returned no usable text.");
                    }
                    else
                    {
                        Debug.LogWarning($"GeminiClient: Attempt {attempt}/{totalAttempts} failed " +
                            $"({request.result}): {request.error}. Response: {request.downloadHandler?.text}");
                    }
                }

                // Back off briefly before retrying (skip the wait after the final attempt).
                if (attempt < totalAttempts && _retryDelaySeconds > 0f)
                    yield return new WaitForSeconds(_retryDelaySeconds);
            }

            Debug.LogError("GeminiClient: All attempts failed; delivering fallback message.");
            onFailure?.Invoke(_fallbackMessage);
        }

        private static string BuildRequestJson(string prompt)
        {
            GeminiRequest payload = new GeminiRequest();
            GeminiContent content = new GeminiContent();
            content.parts.Add(new GeminiPart { text = prompt });
            payload.contents.Add(content);
            return JsonUtility.ToJson(payload);
        }

        private static bool TryParseReply(string json, out string reply)
        {
            reply = null;
            if (string.IsNullOrEmpty(json))
                return false;

            GeminiResponse response;
            try
            {
                response = JsonUtility.FromJson<GeminiResponse>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"GeminiClient: Failed to parse response JSON: {e.Message}");
                return false;
            }

            if (response?.candidates == null || response.candidates.Count == 0)
            {
                if (!string.IsNullOrEmpty(response?.promptFeedback?.blockReason))
                    Debug.LogWarning($"GeminiClient: Prompt blocked by safety filters " +
                        $"({response.promptFeedback.blockReason}).");
                return false;
            }

            GeminiResponseContent content = response.candidates[0].content;
            if (content?.parts == null || content.parts.Count == 0)
                return false;

            StringBuilder sb = new StringBuilder();
            foreach (GeminiPart part in content.parts)
            {
                if (!string.IsNullOrEmpty(part.text))
                    sb.Append(part.text);
            }

            reply = sb.ToString();
            return !string.IsNullOrEmpty(reply);
        }
    }
}
