using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Real-time speech-to-text using ElevenLabs Scribe v2 Realtime over a WebSocket.
    /// Captures the player's microphone, streams 16 kHz PCM16 audio in chunks, detects
    /// end-of-utterance via silence, and surfaces the transcribed text through events.
    ///
    /// GATING: capture only ever starts when <see cref="ConsentManager.IsVoiceModeAvailable"/>
    /// is true, and is torn down immediately if consent is revoked mid-session
    /// (via <see cref="ConsentManager.OnConsentChanged"/>). The negotiation system should
    /// subscribe to <see cref="OnTranscriptionFailed"/> and fall back to scripted dialogue.
    ///
    /// ENDPOINT/FORMAT: sourced from ElevenLabs docs (June 2026) —
    /// https://elevenlabs.io/docs/api-reference/speech-to-text/v-1-speech-to-text-realtime
    /// TODO: confirm the endpoint, model_id and message field names against your account's
    /// current API version before shipping; ElevenLabs may revise these.
    ///
    /// TRANSPORT: System.Net.WebSockets.ClientWebSocket (built into .NET — no extra package).
    /// Works in the Editor, on Mono/IL2CPP Standalone and Android. It does NOT work on WebGL;
    /// if a WebGL build is ever needed this must be swapped for a JS-interop socket.
    /// </summary>
    public class ElevenLabsScribeClient : MonoBehaviour, IManager, ISTTModel
    {
        public static ElevenLabsScribeClient Singleton { get; private set; }

        // --- ElevenLabs Scribe v2 Realtime protocol constants ----------------------
        private const string Endpoint = "wss://api.elevenlabs.io/v1/speech-to-text/realtime";
        private const string ModelId = "scribe_v2_realtime";
        private const int RequiredSampleRate = 16000;   // matches audio_format=pcm_16000

        // Server message_type values we react to.
        private const string MsgPartialTranscript = "partial_transcript";
        private const string MsgCommittedTranscript = "committed_transcript";
        private const string MsgCommittedTranscriptTimestamps = "committed_transcript_with_timestamps";
        private const string MsgSessionStarted = "session_started";

        private const string DefaultConfigResourcePath = "AI/Secrets/ElevenLabsConfig";

        [Header("Configuration")]
        [Tooltip("ElevenLabs credentials. If empty it is loaded from Resources/AI/Secrets/ElevenLabsConfig.")]
        [SerializeField] private ElevenLabsConfig _config;

        // LOCALE — CRITICAL (see docs/MERCHANT_CONTEXT.md): the player speaks Egyptian Arabic,
        // not MSA. Generic Arabic STT is trained heavily on MSA/news audio and performs worse
        // on fast colloquial market speech. Set this to the code that best recognises Egyptian
        // dialect for your account and TEST it on real dialect audio; leaving it empty lets
        // Scribe auto-detect language across 90+ languages.
        [Tooltip("Scribe language_code. MUST be configured/tested for Egyptian Arabic, not " +
                 "generic Arabic/MSA (see docs/MERCHANT_CONTEXT.md). Empty = auto-detect.")]
        [SerializeField] private string _languageCode = "";

        [Header("Capture Mode")]
        [Tooltip("If true, automatically begins listening whenever voice mode becomes available " +
                 "(otherwise call StartListening() — push-to-talk).")]
        [SerializeField] private bool _autoStart = false;

        [Header("Silence Detection")]
        [Tooltip("Seconds of continuous silence after speech before the utterance is finalized.")]
        [SerializeField, Min(0.1f)] private float _silenceThresholdSeconds = 1.2f;
        [Tooltip("Peak sample amplitude (0-1) below which audio is considered silence.")]
        [SerializeField, Range(0f, 0.5f)] private float _silenceAmplitudeThreshold = 0.02f;

        [Header("Reliability")]
        [Tooltip("Seconds to wait for the WebSocket to connect before failing.")]
        [SerializeField, Min(1f)] private float _connectionTimeoutSeconds = 5f;
        [Tooltip("Seconds to wait for the final transcript after committing before failing.")]
        [SerializeField, Min(1f)] private float _responseTimeoutSeconds = 5f;

        /// <summary>Fires once with the final transcribed utterance text.</summary>
        public event Action<string> OnTranscriptionComplete;

        /// <summary>
        /// Fires with a human-readable reason when transcription cannot be completed
        /// (no consent/internet, connect failure, timeout, server error). Catchable by
        /// the negotiation system to fall back to scripted dialogue.
        /// </summary>
        public event Action<string> OnTranscriptionFailed;

        /// <summary>True while the microphone is being captured and streamed.</summary>
        public bool IsListening { get; private set; }

        [Header("Model Pool")]
        [Tooltip("Selection priority within STTManager; higher = preferred.")]
        [SerializeField] private int _priority = 0;

        /// <inheritdoc/>
        public string ModelName => "ElevenLabs Scribe v2";

        /// <inheritdoc/>
        public bool IsAvailable => _config != null && !string.IsNullOrWhiteSpace(_config.ApiKey);

        /// <inheritdoc/>
        public int Priority => _priority;

        // --- Threading: ClientWebSocket runs off the main thread; Unity APIs and events
        //     must run on it. Cross-thread results are queued and drained in Update(). ---
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private readonly ConcurrentQueue<string> _outgoing = new ConcurrentQueue<string>();

        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;
        private volatile bool _socketOpen;

        // --- Microphone state (main-thread only) ---
        private string _micDevice;
        private AudioClip _micClip;
        private float[] _readBuffer;
        private int _clipSamples;
        private int _lastReadPos;
        private bool _speechStarted;
        private float _lastVoiceTime;

        // --- Finalization / timeout state (main-thread only) ---
        private bool _awaitingFinal;
        private float _commitSentTime;
        private bool _sessionEnded;   // guards against double end (timeout + late response)

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

            if (_config == null || string.IsNullOrWhiteSpace(_config.ApiKey))
                Debug.LogWarning("ElevenLabsScribeClient: No ElevenLabsConfig/API key. Listening will fail.");
        }

        private void OnEnable()
        {
            if (ConsentManager.Singleton != null)
                ConsentManager.Singleton.OnConsentChanged += HandleConsentChanged;
        }

        private void Start()
        {
            if (_autoStart) StartListening();
        }

        private void OnDisable()
        {
            if (ConsentManager.Singleton != null)
                ConsentManager.Singleton.OnConsentChanged -= HandleConsentChanged;
        }

        private void OnDestroy()
        {
            AbortSession();
        }

        // --- Public control (push-to-talk) -----------------------------------------

        /// <summary>
        /// Begins capturing and streaming the microphone. No-op if already listening.
        /// Fails fast (via <see cref="OnTranscriptionFailed"/>) if voice mode is unavailable,
        /// the device is offline, no config/mic exists, or the socket can't connect.
        /// </summary>
        public void StartListening()
        {
            if (IsListening) return;

            // Point 7: fail fast on consent/connectivity before touching mic or socket.
            if (ConsentManager.Singleton == null || !ConsentManager.Singleton.IsVoiceModeAvailable())
            {
                Fail("Voice mode unavailable (consent not granted or offline).");
                return;
            }
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                Fail("No internet connection.");
                return;
            }
            if (_config == null || string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                Fail("ElevenLabs API key is not configured.");
                return;
            }
            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                Fail("No microphone device found.");
                return;
            }

            if (!StartMicrophone())
            {
                Fail("Failed to start microphone capture.");
                return;
            }

            IsListening = true;
            _sessionEnded = false;
            _awaitingFinal = false;
            _speechStarted = false;
            _lastVoiceTime = Time.unscaledTime;

            _cts = new CancellationTokenSource();
            _ = RunSocketAsync(_cts.Token);   // fire-and-forget; all exceptions handled inside
        }

        /// <summary>
        /// Stops capturing and asks the server to finalize the current utterance (sends a
        /// commit). The final transcript (or a timeout failure) arrives via the events.
        /// </summary>
        public void StopListening()
        {
            if (!IsListening) return;

            StopMicrophone();
            IsListening = false;

            if (_socketOpen && !_sessionEnded)
            {
                // Send an empty final chunk with commit=true to finalize, then wait for
                // committed_transcript (or the response timeout).
                EnqueueAudioChunk(Array.Empty<byte>(), commit: true);
                _awaitingFinal = true;
                _commitSentTime = Time.unscaledTime;
            }
            else if (!_sessionEnded)
            {
                // Socket never opened — nothing will come back; tear down quietly.
                AbortSession();
            }
        }

        // --- Per-frame: drain cross-thread results, capture audio, check timeouts ---

        private void Update()
        {
            while (_mainThreadQueue.TryDequeue(out Action action))
            {
                action?.Invoke();
            }

            if (IsListening && _socketOpen)
            {
                CaptureAndStreamAudio();
            }

            if (_awaitingFinal && !_sessionEnded
                && Time.unscaledTime - _commitSentTime > _responseTimeoutSeconds)
            {
                Fail("Timed out waiting for transcription response.");
            }
        }

        // --- Microphone capture (main thread) --------------------------------------

        private bool StartMicrophone()
        {
            _micDevice = Microphone.devices[0];
            // 1-second ring buffer, looping, at the rate Scribe expects.
            _micClip = Microphone.Start(_micDevice, true, 1, RequiredSampleRate);
            if (_micClip == null) return false;

            _clipSamples = _micClip.samples;
            _readBuffer = new float[_clipSamples];
            _lastReadPos = 0;

            if (_micClip.frequency != RequiredSampleRate)
            {
                // The device forced a different rate. We tag chunks with the real rate, but
                // audio_format=pcm_16000 expects 16 kHz — quality/recognition may suffer.
                // TODO: resample to 16 kHz if this warning fires on target hardware.
                Debug.LogWarning($"ElevenLabsScribeClient: mic opened at {_micClip.frequency} Hz, " +
                                 $"expected {RequiredSampleRate} Hz.");
            }
            return true;
        }

        private void StopMicrophone()
        {
            if (!string.IsNullOrEmpty(_micDevice) && Microphone.IsRecording(_micDevice))
                Microphone.End(_micDevice);
            _micClip = null;
        }

        private void CaptureAndStreamAudio()
        {
            if (_micClip == null) return;

            int currentPos = Microphone.GetPosition(_micDevice);
            if (currentPos < 0 || currentPos == _lastReadPos) return;

            _micClip.GetData(_readBuffer, 0);

            // Collect samples written since the last read, handling ring-buffer wrap.
            int newCount = currentPos >= _lastReadPos
                ? currentPos - _lastReadPos
                : (_clipSamples - _lastReadPos) + currentPos;

            float[] chunk = new float[newCount];
            float peak = 0f;
            for (int i = 0; i < newCount; i++)
            {
                float sample = _readBuffer[(_lastReadPos + i) % _clipSamples];
                chunk[i] = sample;
                float abs = sample < 0f ? -sample : sample;
                if (abs > peak) peak = abs;
            }
            _lastReadPos = currentPos;

            // Don't stream once we've committed and are waiting for the final result.
            if (!_awaitingFinal)
            {
                EnqueueAudioChunk(FloatsToPcm16(chunk), commit: false);
            }

            // Silence detection: finalize after _silenceThresholdSeconds of quiet, but only
            // once the player has actually started speaking (avoids finalizing on dead air).
            if (peak >= _silenceAmplitudeThreshold)
            {
                _speechStarted = true;
                _lastVoiceTime = Time.unscaledTime;
            }
            else if (_speechStarted && !_awaitingFinal
                     && Time.unscaledTime - _lastVoiceTime >= _silenceThresholdSeconds)
            {
                StopListening();   // graceful finalize
            }
        }

        private static byte[] FloatsToPcm16(float[] samples)
        {
            var bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                short s = (short)Mathf.RoundToInt(clamped * short.MaxValue);
                bytes[i * 2] = (byte)(s & 0xFF);
                bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            return bytes;
        }

        private void EnqueueAudioChunk(byte[] pcm, bool commit)
        {
            var chunk = new ScribeAudioChunk
            {
                message_type = "input_audio_chunk",
                audio_base_64 = pcm.Length > 0 ? Convert.ToBase64String(pcm) : string.Empty,
                commit = commit,
                sample_rate = _micClip != null ? _micClip.frequency : RequiredSampleRate
            };
            _outgoing.Enqueue(JsonUtility.ToJson(chunk));
        }

        // --- WebSocket lifecycle (background) --------------------------------------

        private async Task RunSocketAsync(CancellationToken token)
        {
            try
            {
                _socket = new ClientWebSocket();
                _socket.Options.SetRequestHeader("xi-api-key", _config.ApiKey);

                var uri = new Uri(BuildConnectUrl());

                // Connection timeout (point 7): cancel the connect if it stalls.
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    connectCts.CancelAfter(TimeSpan.FromSeconds(_connectionTimeoutSeconds));
                    await _socket.ConnectAsync(uri, connectCts.Token);
                }

                _socketOpen = true;

                // Skip any audio captured before the socket opened.
                PostToMainThread(() => { if (_micClip != null) _lastReadPos = Microphone.GetPosition(_micDevice); });

                await Task.WhenAll(SendLoopAsync(token), ReceiveLoopAsync(token));
            }
            catch (OperationCanceledException)
            {
                // Expected on teardown or connect timeout; only the latter is a failure.
                if (!_socketOpen) PostFail("WebSocket connection timed out.");
            }
            catch (Exception ex)
            {
                PostFail($"WebSocket error: {ex.Message}");
            }
        }

        private string BuildConnectUrl()
        {
            var sb = new StringBuilder(Endpoint);
            sb.Append("?model_id=").Append(ModelId);
            sb.Append("&audio_format=pcm_16000");
            sb.Append("&commit_strategy=manual");
            if (!string.IsNullOrWhiteSpace(_languageCode))
                sb.Append("&language_code=").Append(Uri.EscapeDataString(_languageCode));
            return sb.ToString();
        }

        private async Task SendLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                if (_outgoing.TryDequeue(out string json))
                {
                    byte[] payload = Encoding.UTF8.GetBytes(json);
                    await _socket.SendAsync(new ArraySegment<byte>(payload),
                        WebSocketMessageType.Text, true, token);
                }
                else
                {
                    await Task.Delay(10, token);
                }
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[8192];
            var message = new StringBuilder();

            while (!token.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                }
                catch (OperationCanceledException) { break; }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                string json = message.ToString();
                message.Clear();
                HandleServerMessage(json);
            }
        }

        private void HandleServerMessage(string json)
        {
            ScribeServerMessage msg;
            try { msg = JsonUtility.FromJson<ScribeServerMessage>(json); }
            catch { return; }
            if (msg == null || string.IsNullOrEmpty(msg.message_type)) return;

            switch (msg.message_type)
            {
                case MsgPartialTranscript:
                case MsgSessionStarted:
                    // Partials/handshake: nothing to surface yet.
                    break;

                case MsgCommittedTranscript:
                case MsgCommittedTranscriptTimestamps:
                    string text = msg.text;
                    PostToMainThread(() => CompleteTranscription(text));
                    break;

                default:
                    // error / auth_error / quota_exceeded / rate_limited / unknown.
                    string reason = string.IsNullOrEmpty(msg.error) ? msg.message_type : msg.error;
                    PostFail($"Scribe error: {reason}");
                    break;
            }
        }

        // --- Completion / failure (main thread) ------------------------------------

        private void CompleteTranscription(string text)
        {
            if (_sessionEnded) return;
            _sessionEnded = true;
            _awaitingFinal = false;

            AbortSession();
            OnTranscriptionComplete?.Invoke(text ?? string.Empty);
        }

        private void Fail(string reason)
        {
            if (_sessionEnded) return;
            _sessionEnded = true;
            _awaitingFinal = false;

            Debug.LogWarning($"ElevenLabsScribeClient: {reason}");
            AbortSession();
            OnTranscriptionFailed?.Invoke(reason);
        }

        // Tears down mic + socket without firing events. Safe to call repeatedly.
        private void AbortSession()
        {
            IsListening = false;
            _socketOpen = false;

            StopMicrophone();

            try { _cts?.Cancel(); } catch { /* already disposed */ }

            ClientWebSocket socket = _socket;
            _socket = null;
            if (socket != null)
            {
                // Dispose off the main thread; close handshake may block briefly.
                Task.Run(() =>
                {
                    try { socket.Abort(); } catch { }
                    socket.Dispose();
                });
            }

            CancellationTokenSource cts = _cts;
            _cts = null;
            cts?.Dispose();
        }

        // --- Consent reaction ------------------------------------------------------

        private void HandleConsentChanged(ConsentState state)
        {
            // Point 2: stop capture immediately if consent/voice is no longer available.
            if (IsListening && (ConsentManager.Singleton == null
                                || !ConsentManager.Singleton.IsVoiceModeAvailable()))
            {
                Fail("Voice mode revoked mid-session.");
            }
            else if (_autoStart && !IsListening
                     && ConsentManager.Singleton != null
                     && ConsentManager.Singleton.IsVoiceModeAvailable())
            {
                StartListening();
            }
        }

        // --- Cross-thread helpers --------------------------------------------------

        private void PostToMainThread(Action action) => _mainThreadQueue.Enqueue(action);
        private void PostFail(string reason) => _mainThreadQueue.Enqueue(() => Fail(reason));

        // --- Serializable DTOs (JsonUtility) ---------------------------------------

        [Serializable]
        private class ScribeAudioChunk
        {
            public string message_type;
            public string audio_base_64;
            public bool commit;
            public int sample_rate;
        }

        [Serializable]
        private class ScribeServerMessage
        {
            public string message_type;
            public string text;
            public string error;
        }
    }
}
