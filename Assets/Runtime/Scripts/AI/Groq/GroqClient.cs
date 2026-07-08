using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace DumyatRuntime
{
    /// <summary>
    /// Singleton client that turns a text prompt into a Groq-generated reply over Groq's
    /// OpenAI-compatible Chat Completions REST API. It owns the request lifecycle: timeout,
    /// network/HTTP error handling and a basic retry, falling back to a configurable message when
    /// every attempt fails. Results are delivered through callbacks so callers stay decoupled
    /// (<see cref="ILlmClient"/>), and it also plugs into the <see cref="LLMManager"/> pool as an
    /// <see cref="ILLMModel"/> alongside <see cref="GeminiClient"/>.
    /// </summary>
    public class GroqClient : MonoBehaviour, IManager, ILlmClient, ILLMModel
    {
        public static GroqClient Singleton { get; private set; }

        private const string Endpoint = "https://api.groq.com/openai/v1/chat/completions";
        private const string DefaultConfigResourcePath = "AI/Secrets/GroqConfig";

        [Header("Configuration")]
        [Tooltip("Groq credentials/model. If left empty it is loaded from Resources/AI/Secrets/GroqConfig.")]
        [SerializeField] private GroqConfig _config;

        [Header("Model Pool")]
        [Tooltip("Selection priority within LLMManager; higher = preferred.")]
        [SerializeField] private int _priority = 0;

        [Header("Reliability")]
        [Tooltip("Seconds to wait for a response before the request is treated as a failure.")]
        [SerializeField, Min(1)] private int _timeoutSeconds = 20;
        [Tooltip("Extra attempts made after the first one fails (1 = one retry). A floor is applied in " +
                 "code so transient rate-limit/overload errors get several retries regardless.")]
        [SerializeField, Min(0)] private int _maxRetries = 1;
        [Tooltip("Base delay before the first retry; later retries back off exponentially from this.")]
        [SerializeField, Min(0f)] private float _retryDelaySeconds = 0.5f;

        // Even if the Inspector leaves _maxRetries low, transient 429/503s (rate-limit / overloaded)
        // usually clear within a few seconds — so make at least this many total attempts on a
        // RETRYABLE failure. Non-retryable errors (bad key/request) still fail fast (see the loop).
        private const int MinAttemptsOnRetryable = 4;
        // Cap the exponential backoff so a bad patch can't stall a turn for too long.
        private const float MaxRetryBackoffSeconds = 4f;
        [Tooltip("Message handed to the failure callback once all attempts are exhausted.")]
        [SerializeField, TextArea] private string _fallbackMessage =
            "Sorry, I can't respond right now. Please try again in a moment.";

        /// <summary>True when a config with a non-empty API key is available.</summary>
        public bool IsConfigured => _config != null && !string.IsNullOrWhiteSpace(_config.ApiKey);

        // --- ILLMModel (pooled by LLMManager) --------------------------------------

        /// <inheritdoc/>
        public string ModelName => _config != null ? _config.ModelName : "Groq";

        /// <inheritdoc/>
        public bool IsAvailable => IsConfigured;

        /// <inheritdoc/>
        public int Priority => _priority;

        /// <summary>
        /// Awaitable text generation for <see cref="LLMManager"/>: bridges the callback-based
        /// <see cref="SendPrompt"/> to a Task, yielding null on failure so the manager can fall back.
        /// </summary>
        public Task<string> GenerateTextAsync(string prompt, bool jsonObject = false)
        {
            var tcs = new TaskCompletionSource<string>();
            SendPrompt(prompt, jsonObject, reply => tcs.TrySetResult(reply), _ => tcs.TrySetResult(null));
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
                _config = Resources.Load<GroqConfig>(DefaultConfigResourcePath);

            if (_config == null)
            {
                Debug.LogWarning($"GroqClient: No GroqConfig assigned and none found at " +
                    $"Resources/{DefaultConfigResourcePath}. Requests will fail until one is provided.");
            }
            else if (string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                Debug.LogWarning("GroqClient: GroqConfig has an empty API key. Set it on the " +
                    "config asset before sending prompts.");
            }
        }

        /// <inheritdoc/>
        public void SendPrompt(string prompt, Action<string> onSuccess, Action<string> onFailure = null)
            => SendPrompt(prompt, jsonObject: false, onSuccess, onFailure);

        /// <summary>
        /// As <see cref="SendPrompt(string,Action{string},Action{string})"/>, but when
        /// <paramref name="jsonObject"/> is true asks Groq to return a single JSON object
        /// (<c>response_format = json_object</c>). The prompt must mention JSON (Groq requirement).
        /// </summary>
        public void SendPrompt(string prompt, bool jsonObject, Action<string> onSuccess, Action<string> onFailure = null)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                Debug.LogWarning("GroqClient: Ignoring empty prompt.");
                onFailure?.Invoke(_fallbackMessage);
                return;
            }

            if (!IsConfigured)
            {
                Debug.LogWarning("GroqClient: Missing API key/config; cannot send prompt.");
                onFailure?.Invoke(_fallbackMessage);
                return;
            }

            StartCoroutine(SendPromptRoutine(prompt, jsonObject, onSuccess, onFailure));
        }

        private IEnumerator SendPromptRoutine(string prompt, bool jsonObject, Action<string> onSuccess, Action<string> onFailure)
        {
            byte[] body = Encoding.UTF8.GetBytes(BuildRequestJson(prompt, jsonObject));
            int totalAttempts = Mathf.Max(_maxRetries + 1, MinAttemptsOnRetryable);

            for (int attempt = 1; attempt <= totalAttempts; attempt++)
            {
                bool retryable = true;   // connection/timeout errors are retryable by default
                long status = 0;

                using (UnityWebRequest request = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST))
                {
                    request.uploadHandler = new UploadHandlerRaw(body);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Authorization", $"Bearer {_config.ApiKey}");
                    request.timeout = _timeoutSeconds; // counts connection errors AND timeouts as failures

                    yield return request.SendWebRequest();
                    status = request.responseCode;

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        if (TryParseReply(request.downloadHandler.text, out string reply))
                        {
                            onSuccess?.Invoke(reply);
                            yield break;
                        }

                        Debug.LogWarning($"GroqClient: Attempt {attempt}/{totalAttempts} succeeded " +
                            $"(HTTP {status}) but returned no usable text.");
                    }
                    else
                    {
                        retryable = IsRetryableStatus(status);
                        Debug.LogWarning($"GroqClient: Attempt {attempt}/{totalAttempts} failed " +
                            $"({request.result}, HTTP {status}): {request.error}. Response: {request.downloadHandler?.text}");
                    }
                }

                // A non-retryable HTTP error (bad key, malformed request, quota exhausted) will not fix
                // itself on retry — stop hammering it and fall through to the next provider quickly.
                if (!retryable)
                {
                    Debug.LogWarning($"GroqClient: HTTP {status} is not retryable; giving up on this provider.");
                    break;
                }

                // Exponential backoff (capped, with jitter) so a transient 429/503 gets a widening gap
                // between retries instead of being hammered every half second.
                if (attempt < totalAttempts)
                    yield return new WaitForSeconds(BackoffSeconds(attempt));
            }

            Debug.LogError("GroqClient: All attempts failed; delivering fallback message.");
            onFailure?.Invoke(_fallbackMessage);
        }

        // HTTP statuses worth retrying: connection/timeout (no HTTP response => status 0), request
        // timeout, rate-limit, and any 5xx server-side error (500/502/503/504 "overloaded"). Any other
        // 4xx is a client/config error (bad key, bad request) that a retry cannot fix.
        private static bool IsRetryableStatus(long status)
        {
            if (status == 0 || status == 408 || status == 429) return true;
            return status >= 500 && status <= 599;
        }

        // Exponential backoff from _retryDelaySeconds, capped at MaxRetryBackoffSeconds, plus a little
        // jitter so parallel providers/turns don't all retry on the same beat.
        private float BackoffSeconds(int attempt)
        {
            float baseDelay = Mathf.Max(0f, _retryDelaySeconds);
            float delay = Mathf.Min(baseDelay * Mathf.Pow(2f, attempt - 1), MaxRetryBackoffSeconds);
            return delay + UnityEngine.Random.Range(0f, 0.25f);
        }

        private string BuildRequestJson(string prompt, bool jsonObject)
        {
            GroqRequest payload = new GroqRequest { model = _config.ModelName };
            payload.messages.Add(new GroqMessage { role = "user", content = prompt });
            payload.response_format.type = jsonObject ? "json_object" : "text";
            return JsonUtility.ToJson(payload);
        }

        private static bool TryParseReply(string json, out string reply)
        {
            reply = null;
            if (string.IsNullOrEmpty(json))
                return false;

            GroqResponse response;
            try
            {
                response = JsonUtility.FromJson<GroqResponse>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"GroqClient: Failed to parse response JSON: {e.Message}");
                return false;
            }

            if (!string.IsNullOrEmpty(response?.error?.message))
            {
                Debug.LogWarning($"GroqClient: API error ({response.error.type}): {response.error.message}");
                return false;
            }

            if (response?.choices == null || response.choices.Count == 0)
                return false;

            GroqMessage message = response.choices[0].message;
            if (message == null || string.IsNullOrEmpty(message.content))
                return false;

            reply = message.content;
            return true;
        }
    }
}
