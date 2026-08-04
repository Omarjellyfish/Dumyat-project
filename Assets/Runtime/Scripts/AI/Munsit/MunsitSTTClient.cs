using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Real-time speech-to-text using the Munsit realtime streaming API over a WebSocket. Munsit is
    /// Arabic-first and covers 25+ dialects without a dialect parameter, which is why it is the
    /// preferred provider for the Egyptian-Arabic merchant negotiation; <see cref="ElevenLabsScribeClient"/>
    /// stays in the pool at a lower <see cref="Priority"/> as the fallback.
    ///
    /// SESSION vs UTTERANCE: the WebSocket is a persistent SESSION — opened once (on the first mic
    /// enable, or explicitly via <see cref="OpenSession"/> when the player enters the market) and KEPT
    /// OPEN across many utterances. The MICROPHONE is a separate toggle
    /// (<see cref="SetMicrophoneEnabled"/>). Munsit has no per-utterance commit: its `CloseStream`
    /// finalises AND closes, so it is only used at session teardown.
    ///
    /// TURN END IS SERVER-AUTHORITATIVE. Munsit runs its own endpointing (plus an optional semantic
    /// `smart_turn` model) and signals the end of a turn with `speech_final: true`. The local adaptive
    /// voice gate (<see cref="MicrophoneStreamCapture"/>) is kept only for the input-level meter,
    /// push-to-talk, dropping leading dead air, and a safety-net timer if the server never endpoints.
    /// <see cref="SilenceThresholdSeconds"/> is handed to the server as its `endpointing` value.
    ///
    /// KEEP-ALIVE IS LOAD-BEARING: Munsit closes the socket with 1011 after 12 seconds with no audio
    /// and no KeepAlive, and the negotiation idles far longer than that while the LLM thinks and the
    /// merchant's TTS plays. The heartbeat lives in <see cref="SendLoopAsync"/> — see the comment there.
    ///
    /// GATING: capture only ever starts when <see cref="IConsentService.IsVoiceModeAvailable"/> is
    /// true, and the whole session is torn down if consent is revoked mid-session.
    ///
    /// ENDPOINT/FORMAT: verified against https://docs.munsit.com/speech-to-text/streaming
    ///
    /// TRANSPORT: System.Net.WebSockets.ClientWebSocket (built into .NET — no extra package).
    /// Works in the Editor, on Mono/IL2CPP Standalone and Android. It does NOT work on WebGL.
    /// </summary>
    public class MunsitSTTClient : MonoBehaviour, ISTTModel, IVoiceCaptureSource
    {
        // Injected consent + STT services and event bus. The STT service supplies the
        // provider-agnostic language code, which Munsit can only honour as "ar".
        private IConsentService _consent;
        private ISTTService _stt;
        private IEventService _events;

        [Inject]
        private void Inject(IConsentService consent, ISTTService stt, IEventService events)
        {
            _consent = consent;
            _stt = stt;
            _events = events;
        }

        // --- Munsit realtime protocol constants ------------------------------------

        // Munsit v1 supports Arabic only; there is no dialect parameter (one model covers 25+ dialects).
        private const string Language = "ar";
        private const int MinEndpointingMs = 100;
        private const int MaxEndpointingMs = 5000;
        private const int MaxHotwords = 200;
        private const int MaxHotwordLength = 40;

        // Server closes with 1011 after 12s of no audio and no KeepAlive. 5s leaves a ~2.4x margin.
        private const double KeepAliveIntervalSeconds = 5.0;

        // Server event types (JSON text frames, discriminated by "type").
        private const string MsgMetadata = "Metadata";
        private const string MsgSpeechStarted = "SpeechStarted";
        private const string MsgResults = "Results";
        private const string MsgUtteranceEnd = "UtteranceEnd";
        private const string MsgError = "Error";

        private const string KeepAliveJson = "{\"type\":\"KeepAlive\"}";
        private const string CloseStreamJson = "{\"type\":\"CloseStream\"}";
        private static readonly byte[] KeepAliveFrame = Encoding.UTF8.GetBytes(KeepAliveJson);

        // Shorter than this and the utterance is a blip — discard it locally rather than paying for it.
        private const float MinUtteranceSeconds = 0.3f;
        // If UtteranceEnd arrives without a speech_final, wait this long for the final before using
        // whatever is buffered.
        private const float UtteranceEndGraceSeconds = 0.25f;
        // A part-filled audio frame may never sit longer than this before being sent.
        private const float PartialFrameStaleSeconds = 0.25f;
        // How long to wait for the server's closing Metadata after sending CloseStream.
        private const float CloseHandshakeSeconds = 0.75f;
        // Queued outbound frames past which we start dropping the oldest audio (~20s at 100ms frames).
        private const int MaxQueuedFrames = 200;

        private const string LogPrefix = "[Munsit STT]";

        [Header("Configuration")]
        [Tooltip("Munsit credentials and connection settings.")]
        [SerializeField] private MunsitConfig _config;

        [Header("Capture Mode")]
        [Tooltip("If true, automatically enables the microphone whenever voice mode becomes available " +
                 "(otherwise call SetMicrophoneEnabled() — push-to-talk / manual).")]
        [SerializeField] private bool _autoStart = false;

        [Header("Silence Detection")]
        [Tooltip("Seconds of silence that end a turn. Sent to Munsit as its 'endpointing' value — the " +
                 "SERVER decides the turn is over. A local timer at (2x this + 1.5s) is only a safety " +
                 "net for the case where the server never endpoints.")]
        [SerializeField, Min(0.1f)] private float _silenceThresholdSeconds = 1.2f;
        [Tooltip("Peak sample amplitude (0-1) below which audio is considered silence, for the local " +
                 "gate that drops leading dead air. Only used when auto-sensitivity is OFF.")]
        [SerializeField, Range(0f, 0.5f)] private float _silenceAmplitudeThreshold = 0.02f;
        // Owned by the VoiceCaptureController, which pushes it here each utterance so there is exactly
        // one Inspector control. Defaults true so the client self-calibrates if used standalone.
        private bool _autoAdjustSensitivity = true;
        [Tooltip("Head-room the auto gate keeps above the tracked noise floor (gate ≈ floor × this). " +
                 "Higher = less sensitive. Advanced tuning.")]
        [SerializeField, Range(1.5f, 6f)] private float _autoSensitivityMargin = 2.5f;

        [Header("Reliability")]
        [Tooltip("Seconds to wait for the WebSocket to connect before failing.")]
        [SerializeField, Min(1f)] private float _connectionTimeoutSeconds = 5f;
        [Tooltip("Seconds to wait for Munsit to send speech_final after we flush the silent tail. " +
                 "The session stays open on timeout.")]
        [SerializeField, Min(1f)] private float _responseTimeoutSeconds = 6f;
        [Tooltip("On a response timeout with nothing finalised, emit the last PARTIAL transcript rather " +
                 "than failing the turn. The text may be unrevised, but a rough transcript beats a " +
                 "silent re-listen that makes the merchant look deaf.")]
        [SerializeField] private bool _emitInterimOnTimeout = true;

        [Header("Streaming")]
        [Tooltip("Milliseconds of audio batched into each outbound binary frame. Munsit wants roughly " +
                 "20-200ms per frame; a raw Unity Update tick is only ~16ms, hence the batching.")]
        [SerializeField, Range(20, 200)] private int _audioFrameMilliseconds = 100;

        [Header("Debug")]
        [Tooltip("Log the full session/utterance lifecycle (including every inbound server frame) to " +
                 "the Console for testing.")]
        [SerializeField] private bool _verboseLogging = false;

        [Tooltip("Write each utterance's audio (exactly the PCM streamed to Munsit, after gating and " +
                 "before the silent tail) to a .wav so mis-transcriptions can be reviewed. Saved under " +
                 "Application.persistentDataPath/<folder>.")]
        [SerializeField] private bool _dumpSttAudio = true;

        [Tooltip("Sub-folder (under Application.persistentDataPath) for the dumped STT .wav files.")]
        [SerializeField] private string _sttAudioFolderName = "STT_Audio";

        [Header("Model Pool")]
        [Tooltip("Selection priority within STTManager; higher = preferred. Above the ElevenLabs Scribe " +
                 "client so Munsit is chosen first and Scribe acts as the fallback.")]
        [SerializeField] private int _priority = 10;

        // --- Events ----------------------------------------------------------------

        /// <summary>
        /// Fires once per utterance with the final transcript, when Munsit reports the player has
        /// finished speaking (speech_final). The session stays open after.
        /// </summary>
        public event Action<string> OnTranscriptionComplete;

        /// <summary>
        /// Fires repeatedly mid-utterance with the latest partial (revisable) transcript, for a live
        /// caption. Display only — never feed it to the LLM / accept-decline scoring.
        /// </summary>
        public event Action<string> OnPartialTranscript;

        /// <summary>
        /// Fires with a human-readable reason when transcription cannot be completed
        /// (no consent/internet, connect failure, timeout, server error).
        /// </summary>
        public event Action<string> OnTranscriptionFailed;

        // --- IVoiceCaptureSource / ISTTModel surface -------------------------------

        /// <summary>True while the microphone is enabled (capturing + streaming an utterance).</summary>
        public bool MicrophoneEnabled { get; private set; }

        /// <inheritdoc/>
        /// <summary>ISTTModel view of capture state — true while the mic is enabled.</summary>
        public bool IsListening => MicrophoneEnabled;

        /// <summary>True while the persistent WebSocket session is open (or connecting), and not closing.</summary>
        public bool IsSessionOpen => (_socketOpen || _connecting) && !_closing;

        /// <summary>Peak amplitude (0-1) of the most recent captured audio chunk; 0 when idle.</summary>
        public float CurrentInputLevel => _capture.CurrentInputLevel;

        /// <summary>Names of the microphone devices currently connected to the system (live).</summary>
        public static string[] AvailableMicrophoneDevices => Microphone.devices;

        /// <summary>
        /// Preferred microphone device name; empty = system default. Applied on the next capture —
        /// toggle the mic off/on to switch devices mid-session.
        /// </summary>
        public string MicrophoneDevice
        {
            get => _microphoneDevice;
            set => _microphoneDevice = value ?? string.Empty;
        }

        /// <summary>
        /// True (default) = voice-activity: the local gate drops leading dead air and the SERVER ends
        /// the turn. False = push-to-talk: the caller finalises by disabling the mic, which flushes a
        /// silent tail so the server still endpoints.
        /// </summary>
        public bool AutoStopOnSilence { get; set; } = true;

        /// <summary>Seconds of silence that end a turn; sent to Munsit as its `endpointing` value.</summary>
        public float SilenceThresholdSeconds
        {
            get => _silenceThresholdSeconds;
            set => _silenceThresholdSeconds = Mathf.Max(0.1f, value);
        }

        /// <summary>Peak amplitude (0-1) below which audio counts as silence for the local gate.</summary>
        public float SilenceAmplitudeThreshold
        {
            get => _silenceAmplitudeThreshold;
            set => _silenceAmplitudeThreshold = Mathf.Clamp(value, 0f, 0.5f);
        }

        /// <summary>Discord-style automatic input sensitivity (adaptive noise-floor gate).</summary>
        public bool AutoAdjustSensitivity
        {
            get => _autoAdjustSensitivity;
            set => _autoAdjustSensitivity = value;
        }

        /// <summary>The tracked ambient noise floor (0-1, peak domain). 0 when idle / not calibrated.</summary>
        public float CurrentNoiseFloor => _capture.CurrentNoiseFloor;

        /// <summary>The effective voice gate the level is currently compared against.</summary>
        public float CurrentGateThreshold => _capture.CurrentGateThreshold;

        /// <inheritdoc/>
        public string ModelName => "Munsit Realtime";

        /// <inheritdoc/>
        /// <remarks>
        /// Key presence only — deliberately NOT internet reachability (that is checked in
        /// <see cref="OpenSession"/>), so being briefly offline does not permanently demote this
        /// provider in the pool. <see cref="_fatalConfigError"/> latches on a non-retryable server
        /// rejection (bad key, bad parameters), which is what hands the next capture to the fallback.
        /// </remarks>
        public bool IsAvailable => !_fatalConfigError
                                   && _config != null
                                   && !string.IsNullOrWhiteSpace(_config.ApiKey)
                                   && !string.IsNullOrWhiteSpace(_config.Endpoint);

        /// <inheritdoc/>
        public int Priority => _priority;

        // --- Threading: ClientWebSocket runs off the main thread; Unity APIs and events
        //     must run on it. Cross-thread results are queued and drained in Update(). ---
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private readonly ConcurrentQueue<OutboundFrame> _outgoing = new ConcurrentQueue<OutboundFrame>();

        private CancellationTokenSource _cts;
        private volatile bool _socketOpen;
        private volatile bool _connecting;
        private volatile bool _closing;
        private long _lastOutboundUtcTicks;

        // Each connection attempt gets a unique epoch. Cross-thread callbacks carry the epoch they were
        // started under and are ignored unless it still matches — this stops a dead connection's late
        // failure from tearing down a freshly reconnected session.
        private int _sessionEpoch;

        // Latched when the server rejects us in a way a reconnect cannot fix (bad key, concurrency cap,
        // insufficient balance, invalid parameters). Makes IsAvailable false so the pool falls through.
        private bool _fatalConfigError;

        // --- Microphone / utterance state (main-thread only) -----------------------
        private string _microphoneDevice = string.Empty;
        private readonly MicrophoneStreamCapture _capture = new MicrophoneStreamCapture();
        private int _streamedSampleCount;   // samples streamed for the current utterance (16 kHz domain)

        // Outbound audio is batched into frames of _audioFrameMilliseconds rather than sent per Update.
        private byte[] _frameBuffer;
        private int _frameFill;
        private float _frameStartTime;
        private bool _backPressureLogged;

        // --- Turn state (main-thread only) -----------------------------------------
        // Munsit force-splits unbroken speech roughly every 60s as is_final WITHOUT speech_final; those
        // fragments continue under a new turn_id, so they are concatenated here and only the terminal
        // speech_final emits. Note there is deliberately NO transcript-settling window (as the Scribe
        // client needs): speech_final is definitive, so waiting for refinements would be pure latency.
        private readonly StringBuilder _turnBuffer = new StringBuilder();
        private string _lastInterim = string.Empty;

        // Whether Munsit has sent ANY Results frame for the current utterance. This is what separates
        // "the server is stalling" (results came, speech_final did not) from "there was nothing to
        // transcribe" (no results at all) — see HandleFinalizeTimeout.
        private bool _resultsSeenThisUtterance;
        private bool _finalizePending;              // silent tail flushed, awaiting speech_final
        private float _finalizeSentTime;
        private float _utteranceEndGraceDeadline;   // 0 = not armed
        private float _teardownDeadline;
        private int _appliedEndpointingMs;

        // Accumulates exactly the PCM streamed for the current utterance (gated audio only, excluding
        // the synthetic silent tail) so a committed utterance can be written to a .wav for debugging.
        private readonly List<byte> _utterancePcm = new List<byte>();

        private void Awake()
        {
            if (_config == null || string.IsNullOrWhiteSpace(_config.ApiKey))
                Debug.LogWarning($"{LogPrefix} No MunsitConfig/API key assigned — this provider will be " +
                                 "skipped and STT will fall back to the next available model.");
        }

        private void OnEnable()
        {
            _events?.Subscribe<ConsentChangedEvent>(HandleConsentChanged);
        }

        private void Start()
        {
            if (_autoStart) SetMicrophoneEnabled(true);
        }

        private void OnDisable()
        {
            _events?.Unsubscribe<ConsentChangedEvent>(HandleConsentChanged);
        }

        private void OnDestroy()
        {
            TeardownSocket();
        }

        // --- Public session control ------------------------------------------------

        /// <summary>
        /// Opens the persistent WebSocket session and keeps it open. Idempotent. Fails fast (via
        /// <see cref="OnTranscriptionFailed"/>) if voice mode is unavailable, offline, or unconfigured.
        /// </summary>
        public bool OpenSession()
        {
            if (_socketOpen || _connecting) return !_closing;

            if (_fatalConfigError)
            {
                NotifyFailed("Munsit previously rejected this configuration; not reconnecting.");
                return false;
            }
            if (_consent == null || !_consent.IsVoiceModeAvailable())
            {
                NotifyFailed("Voice mode unavailable (consent not granted or offline).");
                return false;
            }
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                NotifyFailed("No internet connection.");
                return false;
            }
            if (_config == null || string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                NotifyFailed("Munsit API key is not configured.");
                return false;
            }

            _connecting = true;
            _closing = false;
            _finalizePending = false;
            _cts = new CancellationTokenSource();
            int epoch = ++_sessionEpoch;
            _ = RunSocketAsync(epoch, _cts.Token);   // fire-and-forget; all exceptions handled inside
            Log("Opening session — connecting WebSocket…");
            return true;
        }

        /// <summary>
        /// Closes the persistent session. Sends Munsit's CloseStream so the in-progress turn is
        /// finalised and the closing billing metadata arrives, then tears the socket down. Idempotent.
        /// </summary>
        public void CloseSession()
        {
            if (!_socketOpen && !_connecting && !MicrophoneEnabled) return;

            if (MicrophoneEnabled)
            {
                _capture.Stop();
                MicrophoneEnabled = false;
                FinalizeUtterance("session closing");
            }

            if (_socketOpen && !_closing)
            {
                _closing = true;
                // Give a pending finalisation room to land; otherwise just wait out the handshake.
                _teardownDeadline = Time.unscaledTime + (_finalizePending
                    ? Mathf.Min(_responseTimeoutSeconds, 3f)
                    : CloseHandshakeSeconds);
                EnqueueText(CloseStreamJson);
                Log("Closing session — sent CloseStream, awaiting the final transcript + billing metadata.");
                return;
            }

            Log("Closing session — tearing down mic + socket.");
            TeardownSocket();
        }

        /// <summary>
        /// Clears the latched non-retryable failure so this provider can be tried again without a
        /// domain reload. For the voice-capture debug harness after fixing the key/config.
        /// </summary>
        public void ResetConfigError() => _fatalConfigError = false;

        // --- Public microphone toggle ----------------------------------------------

        /// <summary>
        /// Toggles the microphone. Enabling auto-opens the session (if needed) and begins capturing;
        /// disabling flushes a silent tail so the server endpoints the utterance, and LEAVES THE
        /// SESSION OPEN for the next one.
        /// </summary>
        public void SetMicrophoneEnabled(bool enabled)
        {
            if (enabled) EnableMicrophone();
            else DisableMicrophone();
        }

        private void EnableMicrophone()
        {
            if (MicrophoneEnabled) return;

            // Android needs the RECORD_AUDIO runtime grant before Microphone.devices is populated. The
            // consent flow secures it up front for the negotiation path, but a caller that bypasses
            // consent (e.g. the STT test harness) can reach here without it. Request it and bail this
            // attempt: the async grant can't retroactively fill this call.
            if (!MicrophonePermission.IsGranted)
            {
                MicrophonePermission.Request(_ => { });
                NotifyFailed("Microphone permission not granted yet — requested it; try again once allowed.");
                return;
            }

            if (!IsSessionOpen && !OpenSession()) return;

            // Push the current tuning onto the capture, then re-calibrate the adaptive gate from
            // scratch for this utterance (a new mic / room has a different ambience).
            _capture.LogPrefix = LogPrefix;
            _capture.Device = _microphoneDevice;
            _capture.AutoAdjustSensitivity = _autoAdjustSensitivity;
            _capture.SensitivityMargin = _autoSensitivityMargin;
            _capture.ManualThreshold = _silenceAmplitudeThreshold;
            _capture.DropLeadingSilence = AutoStopOnSilence;
            _capture.ResetCalibration();

            if (!_capture.Start(out string micError))
            {
                NotifyFailed(micError);
                return;
            }

            _finalizePending = false;
            _turnBuffer.Clear();
            _lastInterim = string.Empty;
            _resultsSeenThisUtterance = false;
            _utteranceEndGraceDeadline = 0f;
            _streamedSampleCount = 0;
            _utterancePcm.Clear();
            _backPressureLogged = false;
            EnsureFrameBuffer();
            _frameFill = 0;
            MicrophoneEnabled = true;

            ApplyEndpointingIfChanged();

            Log($"Microphone ENABLED — {(AutoStopOnSilence ? "armed (voice-activity)" : "capturing")} " +
                $"on '{_capture.ResolvedDevice}' (session {(_socketOpen ? "open" : "connecting")}, " +
                $"server endpointing: {EffectiveEndpointingMs()}ms, " +
                $"gate: {(_autoAdjustSensitivity ? $"auto x{_autoSensitivityMargin:0.0} over noise floor" : $"manual amp<{_silenceAmplitudeThreshold}")}).");
        }

        // Explicit mic-off (push-to-talk release, the speech cap, or the master toggle turning off):
        // stop the mic and flush a silent tail so the server finalises. The session stays open.
        private void DisableMicrophone()
        {
            if (!MicrophoneEnabled) return;

            _capture.Stop();
            MicrophoneEnabled = false;

            FinalizeUtterance("microphone disabled");
        }

        // --- ISTTModel compatibility (delegate to the mic toggle) ------------------

        /// <inheritdoc/>
        public void StartListening() => SetMicrophoneEnabled(true);

        /// <inheritdoc/>
        public void StopListening() => SetMicrophoneEnabled(false);

        private void Log(string message)
        {
            if (_verboseLogging) Debug.Log($"{LogPrefix} {message}");
        }

        // --- Per-frame: drain cross-thread results, capture audio, check timers ----

        private void Update()
        {
            while (_mainThreadQueue.TryDequeue(out Action action))
            {
                action?.Invoke();
            }

            if (MicrophoneEnabled && _socketOpen)
            {
                CaptureAndStreamAudio();
            }

            // Never let a part-filled frame sit indefinitely (e.g. the gate closed mid-frame).
            if (_frameFill > 0 && Time.unscaledTime - _frameStartTime > PartialFrameStaleSeconds)
            {
                FlushAudioFrame();
            }

            CheckTimers();
        }

        private void CheckTimers()
        {
            float now = Time.unscaledTime;

            // UtteranceEnd arrived but its speech_final never did (dropped or out of order) — use what
            // we have rather than stalling the negotiation turn.
            if (_utteranceEndGraceDeadline > 0f && now >= _utteranceEndGraceDeadline)
            {
                _utteranceEndGraceDeadline = 0f;
                if (CompleteTurn("UtteranceEnd without speech_final"))
                    Debug.LogWarning($"{LogPrefix} UtteranceEnd arrived without a speech_final — emitted " +
                                     "the buffered transcript.");
            }
            else if (_finalizePending && now - _finalizeSentTime > _responseTimeoutSeconds)
            {
                HandleFinalizeTimeout();
            }

            if (_closing && now >= _teardownDeadline)
            {
                Log("Close handshake timed out — tearing down.");
                TeardownSocket();
            }
        }

        // The silent tail was flushed but Munsit never sent speech_final. Prefer ANY text over failing
        // the turn: a failure makes the merchant re-listen silently, which reads as the merchant
        // ignoring the player.
        private void HandleFinalizeTimeout()
        {
            if (_turnBuffer.Length > 0)
            {
                Debug.LogWarning($"{LogPrefix} No speech_final within {_responseTimeoutSeconds:F1}s — " +
                                 "emitting the transcript finalised so far.");
                CompleteTurn("response timeout (buffered text)");
                return;
            }

            if (_emitInterimOnTimeout && !string.IsNullOrWhiteSpace(_lastInterim))
            {
                Debug.LogWarning($"{LogPrefix} No final transcript within {_responseTimeoutSeconds:F1}s — " +
                                 "emitting the last PARTIAL transcript, which may be unrevised.");
                _turnBuffer.Append(_lastInterim);
                CompleteTurn("response timeout (interim)");
                return;
            }

            // Munsit never sent a SINGLE Results frame for this utterance. That is not a failure — it is
            // what the server returns when the audio held no recognisable speech, which happens on every
            // re-armed listen that only ever captured room noise (the local gate passes noise; Munsit,
            // correctly, does not transcribe it). Reporting it as a failure made a working negotiation
            // log an error after every successful turn and pushed the flow into needless recovery.
            //
            // An EMPTY completion is the honest answer: the player said nothing. The negotiation flow
            // already handles empty transcripts — it simply listens again.
            if (!_resultsSeenThisUtterance)
            {
                Log($"No speech in {_responseTimeoutSeconds:F1}s of audio (Munsit returned no results at " +
                    "all) — treating as an empty turn, not a failure.");
                CompleteTurn("no speech detected", allowEmpty: true);
                return;
            }

            _finalizePending = false;
            FailUtterance($"Munsit sent partial results but no speech_final within " +
                          $"{_responseTimeoutSeconds:F1}s.");
        }

        // --- Microphone capture (main thread) --------------------------------------

        private void CaptureAndStreamAudio()
        {
            if (!_capture.Poll(out float[] samples, out byte[] pcm)) return;

            // Stream only what passes the local gate — no leading dead air, just the utterance.
            // (Skip while a finalisation is in flight so the tail isn't polluted with live audio.)
            if (!_finalizePending && pcm != null)
            {
                AppendAudio(pcm);
                _streamedSampleCount += samples.Length;
                // Keep a copy of EXACTLY what we send, for the optional .wav debug dump.
                if (_dumpSttAudio) _utterancePcm.AddRange(pcm);
            }

            if (_capture.SpeechStartedThisPoll)
            {
                Log($"Speech DETECTED (level {_capture.CurrentInputLevel:F3} >= gate " +
                    $"{_capture.CurrentGateThreshold:F3}, floor {_capture.CurrentNoiseFloor:F3}, " +
                    $"{(_autoAdjustSensitivity ? "auto" : "manual")}) — streaming; the SERVER decides " +
                    $"when the turn ends (endpointing {EffectiveEndpointingMs()}ms).");
                return;
            }

            // Safety net ONLY. Munsit's endpointing/smart_turn normally ends the turn well before this
            // fires; if it does fire, the server-side turn detection is not working as configured.
            if (_capture.LastChunkVoiced || _finalizePending || !AutoStopOnSilence || !_capture.SpeechStarted)
                return;

            float safetyNetSeconds = _silenceThresholdSeconds * 2f + 1.5f;
            if (Time.unscaledTime - _capture.LastVoiceTime < safetyNetSeconds) return;

            Debug.LogWarning($"{LogPrefix} {safetyNetSeconds:F1}s of local silence with no speech_final — " +
                             "Munsit's endpointing/smart_turn did not end the turn. Finalising locally.");
            FinalizeUtterance("local silence safety net");
        }

        /// <summary>
        /// Ends the current utterance. Munsit has no per-utterance commit, so instead of a control
        /// message we flush the buffered audio plus a synthetic tail of DIGITAL SILENCE sized from the
        /// endpointing value — that deterministically trips the server's endpointer, where real ambient
        /// room noise might sit above its floor and never end the turn. The socket stays open.
        /// </summary>
        private void FinalizeUtterance(string reason)
        {
            if (_finalizePending) return;

            FlushAudioFrame();

            float streamedSeconds = _streamedSampleCount / (float)AudioCodec.TargetSampleRate;

            if (_streamedSampleCount == 0)
            {
                Log($"Finalize ({reason}) — no captured voice to send.");
                ResetUtteranceAudio();
                return;
            }
            if (streamedSeconds < MinUtteranceSeconds)
            {
                Log($"Utterance too short ({streamedSeconds:F2}s < {MinUtteranceSeconds}s) — discarded.");
                ResetUtteranceAudio();
                return;
            }
            if (!_socketOpen)
            {
                Log($"Finalize ({reason}) — session not open; discarding {streamedSeconds:F2}s.");
                ResetUtteranceAudio();
                return;
            }

            // Dump BEFORE the tail: digital silence has no diagnostic value in the .wav.
            DumpUtteranceAudio();

            int tailMs = SilentTailMilliseconds();
            StreamSilentTail(tailMs);

            _finalizePending = true;
            _finalizeSentTime = Time.unscaledTime;
            Log($"Utterance finalized ({reason}, {streamedSeconds:F2}s streamed) — flushed a {tailMs}ms " +
                "silent tail, awaiting speech_final. Session stays OPEN.");

            ResetUtteranceAudio();
        }

        private int SilentTailMilliseconds() => Mathf.Clamp(EffectiveEndpointingMs() + 400, 500, 2500);

        private void StreamSilentTail(int tailMs)
        {
            int totalBytes = AudioCodec.TargetSampleRate * 2 * tailMs / 1000;
            int frameBytes = FrameBytes();
            int sent = 0;
            while (sent < totalBytes)
            {
                int n = Mathf.Min(frameBytes, totalBytes - sent);
                EnqueueBinary(new byte[n]);   // zero-initialised = digital silence
                sent += n;
            }
        }

        private void ResetUtteranceAudio()
        {
            _capture.ClearSpeechState();
            _streamedSampleCount = 0;
            _utterancePcm.Clear();
        }

        private void DumpUtteranceAudio()
        {
            if (!_dumpSttAudio || _utterancePcm.Count == 0) return;

            string path = AudioCodec.WriteWavDump(_utterancePcm.ToArray(), AudioCodec.TargetSampleRate,
                _sttAudioFolderName, LogPrefix);
            if (path != null) Log($"STT audio dumped → {path}");
        }

        // --- Outbound framing ------------------------------------------------------

        private int FrameBytes() =>
            AudioCodec.TargetSampleRate * 2 * Mathf.Clamp(_audioFrameMilliseconds, 20, 200) / 1000;

        private void EnsureFrameBuffer()
        {
            int size = FrameBytes();
            if (_frameBuffer == null || _frameBuffer.Length != size)
            {
                _frameBuffer = new byte[size];
                _frameFill = 0;
            }
        }

        // Accumulates PCM into whole frames. A raw Unity Update tick is ~16ms of audio, below Munsit's
        // 20ms floor and mostly WebSocket framing overhead, so chunks are coalesced first.
        private void AppendAudio(byte[] pcm)
        {
            EnsureFrameBuffer();

            int offset = 0;
            while (offset < pcm.Length)
            {
                if (_frameFill == 0) _frameStartTime = Time.unscaledTime;

                int n = Mathf.Min(_frameBuffer.Length - _frameFill, pcm.Length - offset);
                Buffer.BlockCopy(pcm, offset, _frameBuffer, _frameFill, n);
                _frameFill += n;
                offset += n;

                if (_frameFill == _frameBuffer.Length)
                {
                    EnqueueBinary((byte[])_frameBuffer.Clone());
                    _frameFill = 0;
                }
            }
        }

        private void FlushAudioFrame()
        {
            if (_frameFill == 0) return;

            var frame = new byte[_frameFill];
            Buffer.BlockCopy(_frameBuffer, 0, frame, 0, _frameFill);
            _frameFill = 0;
            EnqueueBinary(frame);
        }

        private void EnqueueText(string json) =>
            _outgoing.Enqueue(new OutboundFrame(Encoding.UTF8.GetBytes(json), true));

        private void EnqueueBinary(byte[] pcm)
        {
            // Back-pressure: if the send loop has stalled, drop the OLDEST audio rather than growing
            // the queue without bound (an OOM on a Quest build). Only a leading run of binary frames is
            // dropped, so control frames and their ordering with later audio are preserved.
            if (_outgoing.Count > MaxQueuedFrames)
            {
                int dropped = 0;
                while (_outgoing.Count > MaxQueuedFrames / 2
                       && _outgoing.TryPeek(out OutboundFrame head) && !head.IsText
                       && _outgoing.TryDequeue(out _))
                {
                    dropped++;
                }

                if (dropped > 0 && !_backPressureLogged)
                {
                    _backPressureLogged = true;
                    Debug.LogWarning($"{LogPrefix} Outbound audio backed up past {MaxQueuedFrames} frames " +
                                     $"— dropped {dropped} of the oldest. The connection is stalling and " +
                                     "this turn's transcript will be incomplete.");
                }
            }

            _outgoing.Enqueue(new OutboundFrame(pcm, false));
        }

        // --- WebSocket lifecycle (background) --------------------------------------

        // The socket is a LOCAL owned entirely by this task — never a shared field — so overlapping
        // connections (during a reconnect) can't stomp on each other. Teardown cancels the token,
        // which unwinds the loops and disposes the socket in the finally.
        private async Task RunSocketAsync(int epoch, CancellationToken token)
        {
            ClientWebSocket socket = null;
            try
            {
                socket = new ClientWebSocket();
                // Header rather than the api_key query parameter (which Munsit also accepts, and which
                // WINS over the header): a URL ends up in logs and crash reports, a header does not.
                socket.Options.SetRequestHeader("x-api-key", _config.ApiKey);
                // Protocol-level pings keep the connection alive through proxies. NOTE: these do NOT
                // satisfy Munsit's 12s application-level idle rule — see the KeepAlive in SendLoopAsync.
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

                var uri = new Uri(BuildConnectUrl());

                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    connectCts.CancelAfter(TimeSpan.FromSeconds(_connectionTimeoutSeconds));
                    await socket.ConnectAsync(uri, connectCts.Token);
                }

                // Publish "connected" on the main thread, but only if this attempt is still current.
                PostToMainThread(() =>
                {
                    if (epoch != _sessionEpoch) return;   // superseded/torn down while connecting
                    _socketOpen = true;
                    _connecting = false;
                    // Drop whatever the mic buffered while we were connecting.
                    _capture.ResyncReadHead();
                    Log("WebSocket CONNECTED — session open, streaming when mic enabled.");
                });

                await Task.WhenAll(SendLoopAsync(socket, token), ReceiveLoopAsync(socket, epoch, token));

                // Both loops ended without us cancelling on purpose → the connection dropped.
                if (!token.IsCancellationRequested)
                    PostSessionFail(epoch, "Connection ended unexpectedly.");
            }
            catch (OperationCanceledException)
            {
                // token cancelled = intentional teardown (no failure). Otherwise the linked
                // connect-timeout fired.
                if (!token.IsCancellationRequested)
                    PostSessionFail(epoch, "WebSocket connection timed out.");
            }
            catch (Exception ex)
            {
                PostSessionFail(epoch, $"WebSocket error: {ex.Message}");
            }
            finally
            {
                if (socket != null)
                {
                    try { socket.Abort(); } catch { }
                    socket.Dispose();
                }
            }
        }

        private string BuildConnectUrl()
        {
            // Munsit v1 only supports Arabic. The manager-level language code is provider-agnostic, so
            // say so loudly rather than silently ignoring a mismatch.
            string configuredLanguage = _stt != null ? _stt.LanguageCode : null;
            if (!string.IsNullOrWhiteSpace(configuredLanguage)
                && !configuredLanguage.StartsWith(Language, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"{LogPrefix} STTManager language code is '{configuredLanguage}', but " +
                                 $"Munsit only supports '{Language}' — sending '{Language}'.");
            }

            int endpointing = EffectiveEndpointingMs();
            _appliedEndpointingMs = endpointing;

            var sb = new StringBuilder(_config.Endpoint);
            sb.Append("?encoding=linear16");
            // MANDATORY: Munsit defaults to 8000 Hz. Omitting this decodes our 16 kHz PCM at half speed
            // and returns garbage on perfectly clear audio.
            sb.Append("&sample_rate=").Append(AudioCodec.TargetSampleRate);
            sb.Append("&channels=1");
            sb.Append("&model=").Append(Uri.EscapeDataString(_config.Model));
            sb.Append("&language=").Append(Language);
            sb.Append("&interim_results=").Append(_config.InterimResults ? "true" : "false");
            sb.Append("&endpointing=").Append(endpointing);
            sb.Append("&smart_turn=").Append(_config.SmartTurn ? "true" : "false");

            string hotwords = BuildHotwordsParam();
            if (!string.IsNullOrEmpty(hotwords)) sb.Append("&hotwords=").Append(hotwords);

            sb.Append("&correlation_id=").Append(Guid.NewGuid().ToString("N").Substring(0, 16));
            return sb.ToString();
        }

        // Seconds of silence that end a turn, as Munsit's endpointing parameter.
        private int EffectiveEndpointingMs()
        {
            int ms = _config != null && _config.EndpointingMillisecondsOverride > 0
                ? _config.EndpointingMillisecondsOverride
                : Mathf.RoundToInt(_silenceThresholdSeconds * 1000f);
            return Mathf.Clamp(ms, MinEndpointingMs, MaxEndpointingMs);
        }

        // Retunes the server's endpointing on the live session instead of reconnecting — the
        // VoiceCaptureController pushes SilenceThresholdSeconds before every utterance.
        private void ApplyEndpointingIfChanged()
        {
            int desired = EffectiveEndpointingMs();
            if (!_socketOpen || desired == _appliedEndpointingMs) return;

            EnqueueText($"{{\"type\":\"Configure\",\"endpointing\":{desired}}}");
            Log($"Retuned server endpointing {_appliedEndpointingMs}ms → {desired}ms (Configure).");
            _appliedEndpointingMs = desired;
        }

        // Validates and percent-encodes the configured hotwords. Each entry is escaped individually so
        // the comma separator survives any gateway; Arabic becomes percent-encoded UTF-8.
        private string BuildHotwordsParam()
        {
            if (_config == null) return null;

            if (_config.ModelIgnoresHotwords)
            {
                if (!string.IsNullOrWhiteSpace(_config.Hotwords))
                    Debug.LogWarning($"{LogPrefix} Model '{_config.Model}' ignores hotwords — the " +
                                     "configured vocabulary is NOT being sent.");
                return null;
            }

            string raw = _config.Hotwords;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var sb = new StringBuilder();
            int kept = 0;
            int overflow = 0;
            foreach (string entry in raw.Split(','))
            {
                string word = entry.Trim();
                if (word.Length == 0) continue;

                if (word.Length > MaxHotwordLength)
                {
                    Debug.LogWarning($"{LogPrefix} Hotword \"{word}\" is longer than {MaxHotwordLength} " +
                                     "characters — dropped.");
                    continue;
                }
                if (kept >= MaxHotwords) { overflow++; continue; }

                if (kept > 0) sb.Append(',');
                sb.Append(Uri.EscapeDataString(word));
                kept++;
            }

            if (overflow > 0)
                Debug.LogWarning($"{LogPrefix} More than {MaxHotwords} hotwords configured — {overflow} " +
                                 "trailing entr(ies) dropped.");

            return kept > 0 ? sb.ToString() : null;
        }

        private async Task SendLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            // KEEP-ALIVE. Munsit closes the socket with 1011 after 12 seconds with no audio and no
            // KeepAlive, and the negotiation sits idle far longer than that between turns while the LLM
            // thinks and the merchant's TTS plays. `lastSend` is deliberately a LOCAL: it is born and
            // dies with this connection, so it needs no epoch guard and no reset logic. This lives here
            // rather than in Update() because the main thread stalls on scene loads, GC spikes and an
            // unfocused Editor — 12 seconds is not enough budget to bet on it.
            DateTime lastSend = DateTime.UtcNow;
            Interlocked.Exchange(ref _lastOutboundUtcTicks, lastSend.Ticks);

            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                if (_outgoing.TryDequeue(out OutboundFrame frame))
                {
                    await socket.SendAsync(new ArraySegment<byte>(frame.Payload),
                        frame.IsText ? WebSocketMessageType.Text : WebSocketMessageType.Binary, true, token);
                }
                else if ((DateTime.UtcNow - lastSend).TotalSeconds >= KeepAliveIntervalSeconds)
                {
                    await socket.SendAsync(new ArraySegment<byte>(KeepAliveFrame),
                        WebSocketMessageType.Text, true, token);
                }
                else
                {
                    await Task.Delay(10, token);
                    continue;
                }

                lastSend = DateTime.UtcNow;
                Interlocked.Exchange(ref _lastOutboundUtcTicks, lastSend.Ticks);
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, int epoch, CancellationToken token)
        {
            var buffer = new byte[8192];
            using (var message = new MemoryStream(8192))
            {
                while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result;
                    try
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    }
                    catch (OperationCanceledException) { break; }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        int code = result.CloseStatus.HasValue ? (int)result.CloseStatus.Value : 0;
                        string desc = result.CloseStatusDescription ?? string.Empty;
                        PostToMainThread(() => { if (epoch == _sessionEpoch) HandleServerClose(code, desc); });
                        break;
                    }

                    message.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;

                    // Decode only once the WHOLE message is in hand: an Arabic transcript can straddle
                    // the read boundary, and decoding a partial UTF-8 sequence corrupts the character.
                    string json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    message.SetLength(0);

                    if (result.MessageType == WebSocketMessageType.Text)
                        HandleServerMessage(json, epoch);
                }
            }
        }

        // --- Server messages (parsed off the main thread, applied on it) -----------

        private void HandleServerMessage(string json, int epoch)
        {
            MunsitServerMessage msg;
            try { msg = JsonUtility.FromJson<MunsitServerMessage>(json); }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} Could not parse a server message: {ex.Message}");
                return;
            }
            if (msg == null || string.IsNullOrEmpty(msg.type)) return;

            // Diagnostic: the raw frame is the single most useful thing when a turn stalls.
            Log($"◄ {json}");

            switch (msg.type)
            {
                case MsgMetadata:
                {
                    MunsitServerMessage metadata = msg;
                    PostToMainThread(() => { if (epoch == _sessionEpoch) HandleMetadata(metadata); });
                    break;
                }

                case MsgResults:
                {
                    string transcript = msg.transcript ?? string.Empty;
                    bool isFinal = msg.is_final;
                    bool speechFinal = msg.speech_final;
                    PostToMainThread(() =>
                    {
                        if (epoch == _sessionEpoch) HandleResults(transcript, isFinal, speechFinal);
                    });
                    break;
                }

                case MsgUtteranceEnd:
                    PostToMainThread(() => { if (epoch == _sessionEpoch) HandleUtteranceEnd(); });
                    break;

                case MsgError:
                {
                    int code = msg.code;
                    string message = msg.message;
                    bool recoverable = msg.recoverable;
                    PostToMainThread(() =>
                    {
                        if (epoch == _sessionEpoch) HandleServerError(code, message, recoverable);
                    });
                    break;
                }

                case MsgSpeechStarted:
                default:
                    // SpeechStarted / Gender / Sentiment are informational; the raw frame is already
                    // logged above when verbose logging is on.
                    break;
            }
        }

        private void HandleMetadata(MunsitServerMessage metadata)
        {
            // The closing Metadata (after CloseStream) carries the billing summary instead of the
            // session parameters.
            if (_closing || metadata.turn_count > 0 || metadata.audio_seconds_billed > 0f)
            {
                Debug.Log($"{LogPrefix} Session closed — {metadata.audio_seconds_billed:F1}s billed across " +
                          $"{metadata.turn_count} turn(s). session_id={metadata.session_id}");
                return;
            }

            Debug.Log($"{LogPrefix} Session started — model='{metadata.model}', " +
                      $"session_id={metadata.session_id}, correlation_id={metadata.correlation_id}, " +
                      $"sample_rate={metadata.sample_rate}.");

            if (metadata.sample_rate > 0 && metadata.sample_rate != AudioCodec.TargetSampleRate)
            {
                Debug.LogError($"{LogPrefix} Munsit is decoding at {metadata.sample_rate} Hz but we stream " +
                               $"{AudioCodec.TargetSampleRate} Hz — the sample_rate query parameter did not " +
                               "take. Transcripts will be garbage.");
            }

            if (metadata.dropped_hotwords != null && metadata.dropped_hotwords.Length > 0)
            {
                Debug.LogWarning($"{LogPrefix} Munsit dropped {metadata.dropped_hotwords.Length} hotword(s): " +
                                 $"{string.Join(", ", metadata.dropped_hotwords)}. Fix them in MunsitConfig.");
            }
        }

        private void HandleResults(string transcript, bool isFinal, bool speechFinal)
        {
            // Proof of life for this utterance, whatever the frame turns out to contain.
            _resultsSeenThisUtterance = true;

            if (!isFinal)
            {
                // Revisable partial — caption only. Show the whole utterance so far, not just this
                // fragment, so a force-split mid-utterance doesn't make the caption jump back to empty.
                _lastInterim = transcript;
                string caption = JoinTranscript(_turnBuffer.ToString(), transcript);
                if (!string.IsNullOrWhiteSpace(caption)) OnPartialTranscript?.Invoke(caption);
                return;
            }

            AppendFinal(transcript);
            _lastInterim = string.Empty;

            if (speechFinal)
            {
                // allowEmpty only when WE asked for a finalisation: in continuous/voice-activity mode a
                // blank speech_final is just the server endpointing background noise.
                if (!CompleteTurn("speech_final", allowEmpty: _finalizePending))
                    Log("speech_final with an empty transcript and nothing buffered — ignored.");
                return;
            }

            // is_final WITHOUT speech_final = Munsit force-splitting unbroken speech (~every 60s). The
            // text is stable but the utterance continues under a NEW turn_id, so keep buffering — and
            // treat the split as proof the server is alive, restarting the response-timeout clock.
            Log($"Forced split (is_final without speech_final, {transcript?.Length ?? 0} chars) — " +
                "buffering; the turn continues.");
            _finalizeSentTime = Time.unscaledTime;
        }

        private void HandleUtteranceEnd()
        {
            // Normally this arrives AFTER speech_final, by which point the turn is already emitted and
            // the buffer is empty — nothing to do. It only matters if the speech_final went missing.
            if (_turnBuffer.Length == 0) return;
            if (_utteranceEndGraceDeadline > 0f) return;

            _utteranceEndGraceDeadline = Time.unscaledTime + UtteranceEndGraceSeconds;
        }

        private void HandleServerError(int code, string message, bool recoverable)
        {
            string detail = string.IsNullOrWhiteSpace(message) ? $"code {code}" : $"{message} (code {code})";

            if (recoverable)
            {
                FailUtterance($"Munsit error: {detail}");
                return;
            }

            // A non-retryable rejection of our key or our connection parameters: reconnecting would
            // fail identically, so latch it and let the pool fall through to the next provider.
            if (code == 1008 || code == 4002) _fatalConfigError = true;
            FailSession($"Munsit fatal error: {detail}");
        }

        private void HandleServerClose(int code, string description)
        {
            string suffix = string.IsNullOrEmpty(description) ? "" : $" — {description}";

            switch (code)
            {
                case 1000:
                    if (_closing)
                    {
                        Log($"Server acknowledged CloseStream (1000){suffix}.");
                        TeardownSocket();
                        return;
                    }
                    FailSession($"Munsit closed the session (1000){suffix}.");
                    return;

                case 1008:
                    _fatalConfigError = true;
                    FailSession($"Munsit rejected the session (1008 policy — invalid API key, the " +
                                $"concurrent-session limit, or insufficient balance){suffix}. Falling " +
                                "back to the next available STT provider.");
                    return;

                case 1011:
                {
                    double idle = SecondsSinceLastOutbound();
                    if (idle > 10.0)
                    {
                        Debug.LogError($"{LogPrefix} Closed with 1011 after {idle:F1}s with no outbound " +
                                       "frame — the KeepAlive heartbeat is not running. This is a client bug.");
                    }
                    FailSession($"Munsit closed the session (1011 internal error / idle timeout){suffix}.");
                    return;
                }

                case 4002:
                    _fatalConfigError = true;
                    FailSession($"Munsit rejected the connection parameters (4002){suffix}. Reconnecting " +
                                "with the same URL would fail identically — check MunsitConfig.");
                    return;

                case 4008:
                    Debug.LogError($"{LogPrefix} Closed with 4008 — audio was sent more than 60s ahead of " +
                                   "real time. Suspect the silent tail flush or a back-pressure burst.");
                    FailSession($"Munsit closed the session (4008 audio pacing){suffix}.");
                    return;

                default:
                    FailSession($"Munsit closed the WebSocket ({code}){suffix}.");
                    return;
            }
        }

        // --- Turn buffer / completion (main thread) --------------------------------

        private void AppendFinal(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (_turnBuffer.Length > 0) _turnBuffer.Append(' ');
            _turnBuffer.Append(text.Trim());
        }

        private static string JoinTranscript(string buffered, string tail)
        {
            if (string.IsNullOrWhiteSpace(buffered)) return tail ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tail)) return buffered;
            return $"{buffered.Trim()} {tail.Trim()}";
        }

        /// <summary>
        /// The single point at which a transcript leaves this client — every path routes through here,
        /// so exactly one <see cref="OnTranscriptionComplete"/> fires per utterance by construction.
        /// Returns false (emitting nothing) when there is no transcript and none was expected.
        /// </summary>
        private bool CompleteTurn(string reason, bool allowEmpty = false)
        {
            if (_turnBuffer.Length == 0 && !allowEmpty) return false;

            string text = _turnBuffer.ToString().Trim();
            _turnBuffer.Clear();
            _lastInterim = string.Empty;
            _resultsSeenThisUtterance = false;
            _finalizePending = false;
            _utteranceEndGraceDeadline = 0f;
            // Re-arm the leading-silence gate; without this a continuous session streams ambient room
            // noise forever after the first word.
            _capture.ClearSpeechState();

            Log($"Turn COMPLETE ({reason}, {text.Length} chars): \"{text}\". Session stays OPEN.");
            OnTranscriptionComplete?.Invoke(text);
            return true;
        }

        // --- Failure handling (main thread) ----------------------------------------

        // Pre-session validation failure (consent/config/mic). Nothing to tear down.
        private void NotifyFailed(string reason)
        {
            Debug.LogWarning($"{LogPrefix} {reason}");
            OnTranscriptionFailed?.Invoke(reason);
        }

        // Per-utterance failure. The SESSION stays open — just clear the pending state so the next
        // utterance can proceed on the same connection.
        private void FailUtterance(string reason)
        {
            // A late per-utterance error must not throw away a transcript the server already gave us.
            if (_turnBuffer.Length > 0)
            {
                Debug.LogWarning($"{LogPrefix} {reason} (a transcript had already arrived — using it).");
                CompleteTurn("recovered after a per-utterance error");
                return;
            }

            _finalizePending = false;
            _lastInterim = string.Empty;
            _resultsSeenThisUtterance = false;
            _utteranceEndGraceDeadline = 0f;
            Debug.LogWarning($"{LogPrefix} {reason}");
            OnTranscriptionFailed?.Invoke(reason);
        }

        // Fatal SESSION failure (socket died / server closed). Tears the whole session down.
        private void FailSession(string reason)
        {
            bool notify = _finalizePending || MicrophoneEnabled || _connecting || _socketOpen;
            Debug.LogWarning($"{LogPrefix} {reason}");

            // The socket dying while we hold a transcript still leaves us with a usable turn — deliver
            // it instead of failing, then tear the session down quietly.
            bool delivered = CompleteTurn("session ended with a transcript in hand");

            TeardownSocket();
            if (notify && !delivered) OnTranscriptionFailed?.Invoke(reason);
        }

        // Tears down mic + socket without firing events. Safe to call repeatedly. Bumping the epoch
        // invalidates any in-flight callbacks from the connection being torn down. Cancelling the token
        // unwinds RunSocketAsync, which disposes its own socket in its finally.
        private void TeardownSocket()
        {
            _sessionEpoch++;
            MicrophoneEnabled = false;
            _socketOpen = false;
            _connecting = false;
            _closing = false;
            _finalizePending = false;
            _utteranceEndGraceDeadline = 0f;
            _turnBuffer.Clear();
            _lastInterim = string.Empty;
            _resultsSeenThisUtterance = false;
            _appliedEndpointingMs = 0;

            _capture.Stop();

            // Drop any queued audio so a stale backlog isn't streamed into the NEXT connection.
            while (_outgoing.TryDequeue(out _)) { }
            _frameFill = 0;
            _streamedSampleCount = 0;
            _utterancePcm.Clear();

            CancellationTokenSource cts = _cts;
            _cts = null;
            try { cts?.Cancel(); } catch { /* already disposed */ }
            cts?.Dispose();
        }

        // --- Consent reaction ------------------------------------------------------

        private void HandleConsentChanged(ConsentChangedEvent evt)
        {
            // Stop the whole session immediately if consent/voice is no longer available.
            if (IsSessionOpen && (_consent == null || !_consent.IsVoiceModeAvailable()))
            {
                FailSession("Voice mode revoked mid-session.");
            }
            else if (_autoStart && !MicrophoneEnabled
                     && _consent != null
                     && _consent.IsVoiceModeAvailable())
            {
                SetMicrophoneEnabled(true);
            }
        }

        // --- Cross-thread helpers --------------------------------------------------

        private void PostToMainThread(Action action) => _mainThreadQueue.Enqueue(action);

        // Only fails the session if the reporting connection is still the current one — a stale failure
        // from a dropped connection must not tear down a freshly reconnected session.
        private void PostSessionFail(int epoch, string reason) =>
            _mainThreadQueue.Enqueue(() => { if (epoch == _sessionEpoch) FailSession(reason); });

        private double SecondsSinceLastOutbound()
        {
            long ticks = Interlocked.Read(ref _lastOutboundUtcTicks);
            if (ticks == 0) return 0.0;
            return (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
        }

        // --- Outbound frame ---------------------------------------------------------

        // Audio and control messages share ONE queue so a Configure frame stays ordered ahead of the
        // audio it governs, and CloseStream stays behind everything already buffered.
        private readonly struct OutboundFrame
        {
            public readonly byte[] Payload;
            public readonly bool IsText;

            public OutboundFrame(byte[] payload, bool isText)
            {
                Payload = payload;
                IsText = isText;
            }
        }

        // --- Serializable DTO (JsonUtility) -----------------------------------------

        // Only the fields we act on are declared; JsonUtility ignores the rest. This is deliberate —
        // Munsit sends "confidence": null on interim Results, and a declared float would choke on it.
        // Also NOT declared: words[], last_word_end, channel, ts, language, protocol_version, channels.
        [Serializable]
        private class MunsitServerMessage
        {
            public string type;
            public string session_id;
            public string correlation_id;
            public string model;
            public string transcript;
            public string message;              // Error
            public bool is_final;               // Results
            public bool speech_final;           // Results
            public bool recoverable;            // Error
            public int code;                    // Error
            public int sample_rate;             // opening Metadata
            public int turn_count;              // closing Metadata
            public float audio_seconds_billed;  // closing Metadata
            public string[] dropped_hotwords;   // opening Metadata
        }
    }
}
