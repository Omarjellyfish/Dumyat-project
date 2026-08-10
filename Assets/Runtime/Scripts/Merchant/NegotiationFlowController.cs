using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Orchestrates a single fish negotiation end-to-end, tying together consent, deterministic
    /// pricing, speech-to-text, the LLM dialogue layer, and text-to-speech. This is the most
    /// stateful, async-heavy script in the system, so it is written for CLARITY and DEFENSIVE
    /// recovery over cleverness: every external call (STT, LLM, TTS) has a failure path that keeps
    /// the player moving instead of frozen, and every state transition is logged with a
    /// "[Negotiation]" prefix.
    ///
    /// Authority split (deliberate):
    /// - Deterministic game logic (<see cref="PriceNegotiationCalculator"/>) owns the OPENING offer,
    ///   the per-turn deception roll, and the final success-rate score.
    /// - The LLM (<see cref="MerchantNegotiationLLM"/>) owns per-turn DIALOGUE and, per spec, the
    ///   per-turn ACTION (Accept/Raise/Hold/Walk/Clarify); its proposed numbers are already clamped
    ///   to the merchant's real economics inside the LLM layer before we apply them. The
    ///   deterministic <c>EvaluateCounterOffer</c> is intentionally NOT used to drive turns here —
    ///   it remains available if you later want to gate/override the LLM's action.
    ///
    /// Lifecycle handoff: the <see cref="MerchantApproachCoordinator"/> calls <see cref="BeginNegotiation"/>;
    /// when the negotiation ends this calls <c>merchant.EndNegotiation(phase)</c>, which the coordinator
    /// listens to in order to release its one-at-a-time lock and start the cooldown.
    /// </summary>
    public class NegotiationFlowController : MonoBehaviour, INegotiationService
    {
        private const string LogPrefix = "[Negotiation]";

        // Generic recoverable-failure line (STT/LLM glitch). Egyptian Arabic + English subtitle.
        private const string FallbackArabic = "آسف، ممكن تقول تاني؟";
        private const string FallbackEnglish = "Sorry, can you say that again?";

        // Player-facing notice shown (via the subtitle path) when voice fails specifically because the OS
        // microphone permission is missing — the player wants voice (granted the in-app mic consent) but
        // has not allowed the system RECORD_AUDIO grant, so we point them at the headset's app settings.
        private const string MicPermissionNoticeEnglish =
            "Microphone access is off — enable it for this app in your headset settings to haggle by voice.";
        private const string MicPermissionNoticeArabic =
            "إذن المايك مقفول — فعّله للّعبة من إعدادات النظارة عشان تفاصل بصوتك.";

        // Seconds to hold the mic-permission notice on the merchant's subtitle before the negotiation
        // tears down (Cleanup clears the subtitle), so the player has time to read it.
        private const float MicNoticeSeconds = 4f;

        // Stop looping the recover-and-relisten path forever if every attempt keeps failing.
        private const int MaxConsecutiveRecoveries = 3;

        // Cap on SILENT re-listens while waiting for the player (blank captures / transient STT
        // glitches) before we quietly hand off. This only guards against noise/echo/glitch loops —
        // genuine silence produces no STT events at all, so the merchant simply stays quiet.
        private const int MaxSilentRetries = 8;

        // Greeting policy: the FIRST conversation with a merchant always opens with an Egyptian-style
        // greeting; on later conversations a greeting is used only this fraction of the time.
        private const float GreetingChanceAfterFirst = 0.4f;

        [Header("Limits")]
        [Tooltip("Hard ceiling on how many player turns one sale may take, protecting the LLM/TTS " +
                 "usage budget. The merchant gives its final warning on the second-to-last turn and " +
                 "walks away on the last. Only PROCESSED turns count — silent/failed captures are " +
                 "capped separately. 0 = no limit.")]
        [SerializeField, Min(0)] private int _maxPlayerTurns = 20;

        [Header("Placement (scale sale)")]
        [Tooltip("Seconds to wait for the player to load the scale (or change it) before nudging them " +
                 "again to place their fish.")]
        [SerializeField, Min(0.5f)] private float _placementIdleSeconds = 15f;

        [Tooltip("How many times to re-arm the mic while waiting for the player's answer to \"is that " +
                 "your whole catch?\" before re-asking. Each attempt lasts until they speak OR the STT " +
                 "session idle-closes (~15s), so this multiplies how long the merchant patiently waits.")]
        [SerializeField, Min(1)] private int _maxAnswerListens = 3;

        [Tooltip("Give-up guard: after this many unproductive placement rounds (no fish loaded, or no " +
                 "clear \"that's all\" confirmation) the merchant abandons the sale.")]
        [SerializeField, Min(1)] private int _maxPlacementPrompts = 8;

        [Header("Sale Confirmation (scale sale)")]
        [Tooltip("ON: after the player accepts a bundle price, the merchant reads the per-fish price " +
                 "summary back and waits for the player to confirm it before sealing the deal " +
                 "(accept > summary > confirm > close). OFF: acceptance seals the deal immediately — the " +
                 "summary read-back and its confirmation are skipped (accept > close).")]
        [SerializeField] private bool _enableFishSummary = true;

        // Injected AI / consent / stats services (replace the former manager singletons).
        private IConsentService _consent;
        private IMerchantNegotiationService _negotiation;
        private ITTSService _tts;
        private IPlayerStatsService _playerStats;
        private IEventService _eventService;

        [Inject]
        private void Inject(IConsentService consent, IMerchantNegotiationService negotiation,
            ITTSService tts, IPlayerStatsService playerStats, IEventService eventService)
        {
            _eventService = eventService;
            _consent = consent;
            _negotiation = negotiation;
            _tts = tts;
            _playerStats = playerStats;
        }

        [Header("Config")]
        [Tooltip("Global merchant system config (guard-rails, cooldown). Feeds the price calculator.")]
        [SerializeField] private MerchantSystemConfig _systemConfig;

        [Tooltip("AudioSource used for merchant speech when the merchant GameObject has none of its own.")]
        [SerializeField] private AudioSource _fallbackAudioSource;

        [Header("Behavior Graph")]
        [Tooltip("Optional Unity Behavior agent that runs the negotiation as a visual Behavior graph. " +
                 "When assigned (with a graph), BeginNegotiation drives the graph and the graph's " +
                 "Action/Condition nodes call the Bt_* step methods on this controller. When empty, " +
                 "the legacy coded flow runs instead — so the game still works before the graph is wired.")]
        [SerializeField] private Unity.Behavior.BehaviorGraphAgent _agent;

        [Header("Voice Capture")]
        [Tooltip("Shared controller used to listen for the player (capture mode, VAD tuning, file " +
                 "logging, etc.). Auto-resolved from the scene or created if left empty.")]
        [SerializeField] private VoiceCaptureController _voiceCapture;

        [Header("Fish Inventory")]
        [Tooltip("Optional: a component implementing IFishInventory that supplies the fish to sell. " +
                 "If empty, one is searched for in the scene; otherwise the debug fish below is used.")]
        [SerializeField] private MonoBehaviour _fishInventorySource;

        [Header("Opening idiom")]
        [Tooltip("Optional bank of proverbs a good-natured merchant may open with. The asset decides " +
                 "which merchants are mild enough and how often any of them actually says one. Leave " +
                 "empty to fall back to the market-wide bank on MerchantSystemConfig (which the scripted " +
                 "sale also uses); with neither assigned, nobody says one.")]
        [SerializeField] private MerchantIdiomsSO _idioms;

        [Header("Scripted (offline) fallback")]
        [Tooltip("ON: skip the AI voice flow entirely and always run the SCRIPTED (button-based, no-AI) sale. " +
                 "Use for offline builds or to test the scripted path with consent/network available.")]
        [SerializeField] private bool _forceScriptedMode;

        [Tooltip("Scripted-sale service that takes over when the AI voice flow is unavailable (consent " +
                 "declined, offline, mic/LLM/STT failure) or when Force Scripted Mode is on. Auto-resolved " +
                 "from the scene if left empty; if none exists the negotiation just ends cleanly.")]
        [SerializeField] private MonoBehaviour _scriptedSaleSource;

        [Header("Live captions")]
        [Tooltip("Publish the player's live PARTIAL speech transcript as a player-side caption while they " +
                 "talk (PlayerPartialTranscriptEvent → PlayerCaptionPresenter). Display only — never feeds " +
                 "the negotiation decision. Needs an Arabic-capable font on the caption panel; off by default.")]
        [SerializeField] private bool _showPlayerPartials;

        private IScriptedSaleService _scriptedSale;
        private bool _scriptedSaleResolved;

        private PriceNegotiationCalculator _calculator;
        private NegotiationState _state;
        private MerchantNPC _activeMerchant;
        private AudioSource _activeAudioSource;
        private readonly FishInventoryResolver _fishInventory = new FishInventoryResolver();
        private bool _busy;
        private bool _subscribed;
        private int _consecutiveRecoveries;
        private int _silentRetries;   // blank captures / transient STT glitches while WAITING for the player
        private FishData _pendingFallbackFish;   // fish carried across the mic-permission notice delay

        // --- Behavior-graph step state (used only on the BT path; see the Bt_* methods) ---
        private string _pendingLine;                  // merchant line waiting to be spoken by a node
        private bool _lineFinished;                   // set true when the current TTS line finishes/fails
        private bool _transcriptReady;                // set true when a player transcript has arrived
        private bool _captureFailed;                  // set true when STT capture failed
        private string _lastTranscript = "";          // the most recent player transcript
        private MerchantTurnResult _lastResult;       // the most recent validated LLM turn result

        // --- Scale (multi-fish) flow state -----------------------------------------
        private FishFumbleReason? _pendingFumble;                 // a fumble to quip about while awaiting placement
        private TaskCompletionSource<string> _listenTcs;          // bridges one turn-based capture to an await
        private TaskCompletionSource<bool> _speakTcs;             // bridges one TTS line to an await
        private TaskCompletionSource<bool> _placementSignal;      // wakes the placement wait on a fumble / scale change

        // A called-out price waiting to land on the summary card the instant the merchant's TTS finishes
        // voicing it (armed by PublishOffer, flushed from PlayMerchantLine). Skipped when there is no live
        // voice — then PublishOffer publishes at once. See PublishOffer / FlushPendingOffer.
        private bool _offerPending;
        private float _offerPendingAmount;

        /// <summary>True once a catch has been snapshotted from the scale — the scale-based bundle sale.</summary>
        private bool IsBundleSale => _state != null && _state.fishBundle != null && _state.fishBundle.Count > 0;

        /// <summary>True when the negotiation is driven by a Unity Behavior graph (else the legacy flow).</summary>
        public bool UseBehaviorGraph => _agent != null && _agent.Graph != null;

        /// <summary>The merchant this negotiation is with (set before the graph starts). For nodes.</summary>
        public MerchantNPC ActiveMerchant => _activeMerchant;

        /// <summary>Set by a turn result / recovery when the negotiation should stop looping. For the graph.</summary>
        public bool IsTerminal { get; private set; }

        /// <summary>The resolved outcome once <see cref="IsTerminal"/> is set. For the graph / resolve node.</summary>
        public NegotiationPhase Outcome { get; private set; }

        /// <summary>True when the current merchant TTS line has finished (or failed). Polled by a node.</summary>
        public bool LineFinished => _lineFinished;

        /// <summary>True once a player transcript has arrived for the current listen. Polled by a node.</summary>
        public bool TranscriptReady => _transcriptReady;

        /// <summary>True when the current listen failed (STT/capture error). Polled by a node.</summary>
        public bool CaptureFailed => _captureFailed;

        /// <summary>True while a negotiation (AI or scripted-fallback handoff) is in progress.</summary>
        public bool IsNegotiating => _busy;

        private void Awake()
        {
            _calculator = new PriceNegotiationCalculator(_systemConfig);

            // Auto-resolve the Behavior agent from THIS GameObject when left unassigned. The graph's
            // nodes resolve the controller via GetComponent on the agent's own GameObject, so the agent
            // must live on the same object as the controller — enforce that by construction here.
            if (_agent == null) TryGetComponent(out _agent);
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        // --- Entry point (called by MerchantApproachCoordinator) -------------------------------

        /// <summary>
        /// Starts a negotiation with <paramref name="merchant"/>. Gates on consent/connectivity
        /// (point 1): if voice mode is unavailable it hands straight to the scripted fallback;
        /// otherwise it runs the AI flow.
        /// </summary>
        public void BeginNegotiation(MerchantNPC merchant)
        {
            if (merchant == null)
            {
                Debug.LogWarning($"{LogPrefix} BeginNegotiation called with a null merchant.");
                return;
            }
            if (_busy)
            {
                Debug.LogWarning($"{LogPrefix} Already negotiating; ignoring request for '{merchant.name}'.");
                return;
            }

            _activeMerchant = merchant;
            _consecutiveRecoveries = 0;
            _silentRetries = 0;
            ResetStepState();

            if (merchant.Config == null)
            {
                Debug.LogWarning($"{LogPrefix} Merchant '{merchant.name}' has no MerchantConfig; aborting.");
                Cleanup(NegotiationPhase.Declined);
                return;
            }

            // Prefer the merchant's own AudioSource (spatial VR audio); fall back to the shared one.
            _activeAudioSource = merchant.GetComponent<AudioSource>();
            if (_activeAudioSource == null) _activeAudioSource = _fallbackAudioSource;

            // FORCE-SCRIPTED (no graph): skip the AI stack and run the button-based scripted sale directly.
            // When a Behavior graph is assigned it OWNS this decision instead — its "Use Online Selling"
            // condition already honours Force Scripted Mode — so only short-circuit here when there is no
            // graph to make the call.
            if (_forceScriptedMode && !UseBehaviorGraph)
            {
                _busy = true;
                Debug.Log($"{LogPrefix} Force-scripted mode — scripted sale for '{NameOf(merchant)}'.");
                TriggerScriptedFallback(merchant, null);
                return;
            }

            // BEHAVIOR-GRAPH PATH: the graph owns the ONLINE/OFFLINE switch. Its "Use Online Selling"
            // condition guards the AI branch and falls through to the "Run Offline Sale" branch (the
            // scripted, button-based sale via Bt_RunScriptedSaleAsync) when online selling isn't available
            // or is force-disabled. Start the graph and let it decide; the nodes call the Bt_* steps below.
            // IMPORTANT: the graph MUST include that offline branch, or an offline player falls through with
            // no sale — see Merchant/Behavior/README.md for the required shape.
            if (UseBehaviorGraph)
            {
                _busy = true;
                Subscribe();
                _agent.Restart();
                Debug.Log($"{LogPrefix} Begin BT negotiation with '{NameOf(merchant)}'.");
                return;
            }

            // CODED PATH (no graph). SCALE-BASED BUNDLE when the merchant has a sell scale; otherwise the
            // legacy single-fish flow. The catch comes from the scale, so there is NO single fish to resolve
            // up front for a bundle. The consent gate below routes voice-unavailable to the scripted
            // fallback, so the offline case still sells even without a graph.
            bool bundle = merchant.SellScale != null;

            FishData fish = null;
            if (!bundle)
            {
                fish = ResolveFishToSell();
                if (fish == null)
                {
                    Debug.LogWarning($"{LogPrefix} No fish to sell; aborting with '{NameOf(merchant)}'.");
                    Cleanup(NegotiationPhase.Declined);
                    return;
                }
            }

            // Voice-ready / no-voice continuations, so the one consent gate below serves both coded paths.
            void ProceedVoice()
            {
                if (bundle) StartBundleNegotiationCoded(merchant);
                else StartAiNegotiation(merchant, fish);
            }
            void ProceedNoVoice()
            {
                if (bundle) TriggerScriptedFallback(merchant, fish);
                else FallbackAfterVoiceUnavailable(merchant, fish);
            }

            // POINT 1: consent + connectivity gate for the whole AI path.
            if (_consent == null)
            {
                Debug.Log($"{LogPrefix} No ConsentManager — scripted fallback for '{NameOf(merchant)}'.");
                TriggerScriptedFallback(merchant, fish);
                return;
            }

            if (_consent.IsVoiceModeAvailable())
            {
                ProceedVoice();
                return;
            }

            ConsentState consent = _consent.GetConsentStatus();

            // The player already granted in-app consent and is online, so the only thing missing is the
            // OS microphone permission (never requested on this device, or revoked in system settings).
            // Re-request it directly rather than re-showing the whole consent panel, and take the AI path
            // only if it comes back granted.
            bool inAppGranted = consent.MicConsent == ConsentStatus.Granted
                                && consent.InternetConsent == ConsentStatus.Granted;
            bool online = Application.internetReachability != NetworkReachability.NotReachable;
            if (inAppGranted && online)
            {
                Debug.Log($"{LogPrefix} Voice consent granted but OS mic permission missing — requesting it for '{NameOf(merchant)}'.");
                MicrophonePermission.Request(granted =>
                {
                    if (_activeMerchant != merchant) return; // torn down while the OS prompt was up

                    if (granted && _consent != null && _consent.IsVoiceModeAvailable())
                        ProceedVoice();
                    else
                        ProceedNoVoice();
                });
                return;
            }

            // Voice unavailable. If the player has simply never been ASKED, this first approach is
            // the natural moment to ask: show the consent prompt (ConsentPromptUI) and continue on
            // whichever path their answer allows. Only a real decline / missing connectivity falls
            // back to scripted dialogue.
            bool neverAsked = consent.MicConsent == ConsentStatus.NotAsked
                              || consent.InternetConsent == ConsentStatus.NotAsked;
            if (neverAsked)
            {
                Debug.Log($"{LogPrefix} Consent not asked yet — prompting before negotiating with '{NameOf(merchant)}'.");
                _consent.RequestConsent((mic, internet) =>
                {
                    if (_activeMerchant != merchant) return; // torn down while the prompt was up

                    if (_consent != null && _consent.IsVoiceModeAvailable())
                    {
                        ProceedVoice();
                    }
                    else
                    {
                        Debug.Log($"{LogPrefix} Consent declined/unavailable — scripted fallback for '{NameOf(merchant)}'.");
                        ProceedNoVoice();
                    }
                });
                return;
            }

            Debug.Log($"{LogPrefix} Voice mode unavailable — scripted fallback for '{NameOf(merchant)}'.");
            ProceedNoVoice();
        }

        // Kicks off the actual AI negotiation once the fish and consent are both settled.
        private void StartAiNegotiation(MerchantNPC merchant, FishData fish)
        {
            _state = new NegotiationState
            {
                currentMerchant = merchant.Config,
                fishBeingSold = fish,
                fishCatalog = ResolveFishCatalog(),
                state = NegotiationPhase.Approaching,
                turnCount = 0
            };
            _busy = true;
            Subscribe();
            _voiceCapture?.OpenSession();   // warm the STT session while the opening line is generated

            Debug.Log($"{LogPrefix} Begin AI negotiation with '{NameOf(merchant)}' over {fish.fishType}.");
            RunOpeningFlow(merchant, fish);
        }

        // --- Coded bundle flow (scale sale WITHOUT a Behavior graph) ---------------
        //
        // Drives the exact same Bt_* steps the Behavior graph would, so the multi-fish scale sale runs
        // end-to-end before any graph asset is authored. Mirrors the scale-flow graph shape documented in
        // Behavior/README.md: Setup -> Placement -> Opening -> haggle loop -> Sale-confirm -> Resolve.
        // Reached from BeginNegotiation only when the merchant has a SellScale and no graph is assigned.

        private async void StartBundleNegotiationCoded(MerchantNPC merchant)
        {
            _busy = true;
            Subscribe();                 // Bt_Setup opens the STT session
            Debug.Log($"{LogPrefix} Begin coded bundle negotiation with '{NameOf(merchant)}'.");
            await Bt_RunBundleSaleAsync();
        }

        /// <summary>
        /// Runs the ENTIRE scale/bundle sale as one coarse step: consent gate -> Setup -> Placement ->
        /// Opening -> haggle loop -> Sale-confirm -> Resolve, doing its own teardown (Resolve / scripted
        /// fallback / Cleanup) in every branch. Shared by BOTH the coded no-graph path
        /// (<see cref="StartBundleNegotiationCoded"/>) AND the Behavior graph's "Run Bundle Sale" node, so
        /// the two paths run one implementation with no drift. Assumes <c>_busy</c> is set and
        /// <see cref="Subscribe"/> has run (BeginNegotiation does that on the graph path). Always ends in a
        /// terminal state. Returns true if it resolved normally, false if it fell back / errored.
        /// </summary>
        public async Task<bool> Bt_RunBundleSaleAsync()
        {
            try
            {
                // Consent gate (the graph path skips BeginNegotiation's gate): no voice -> scripted fallback.
                if (!Bt_ConsentAvailable()) { if (_busy) Bt_ScriptedFallback(); return false; }

                if (!Bt_Setup()) { if (_busy) Cleanup(NegotiationPhase.Declined); return false; }

                // Placement: welcome, wait for the player to load the scale (quips at fumbles). If they
                // never load it, hand to the scripted fallback (the graph's root-Selector behaviour).
                if (!await Bt_RunPlacementPhaseAsync()) { if (_busy) Bt_ScriptedFallback(); return false; }
                if (!_busy) return false;

                // Opening: acknowledge the catch + opening bundle total, then speak it.
                if (!await Bt_RunOpeningAsync()) { if (_busy) Bt_ScriptedFallback(); return false; }
                if (!await SpeakPendingAsync()) return false;

                // Haggle the single bundle total (same turn loop as the graph, driven here in code).
                await RunCodedHaggleLoopAsync();
                if (!_busy) return false;

                // Sale-confirm: per-fish summary -> confirm -> re-verify the scale -> pay out. Only when a
                // price was actually agreed (the haggle may have ended in a walk-away / decline instead).
                if (IsBundleSale && _state != null && _state.stage == NegotiationStage.AwaitingSaleConfirm)
                {
                    if (!await Bt_RunSaleConfirmPhaseAsync()) { if (_busy) Bt_Resolve(); return true; }
                    if (!_busy) return false;
                }

                Bt_Resolve();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} Bundle sale flow error: {e}");
                if (_busy) Cleanup(NegotiationPhase.Declined);
                return false;
            }
        }

        // The haggle turn loop for the coded bundle flow: listen -> process -> apply -> speak, until a
        // price is agreed (stage -> AwaitingSaleConfirm, which makes NegotiationOngoing false) or the
        // merchant walks / the player declines. Mirrors the graph's Repeat-While-Negotiation-Ongoing loop.
        private async Task RunCodedHaggleLoopAsync()
        {
            int silent = 0;
            while (NegotiationOngoing)
            {
                string transcript = await ListenAsync();
                if (!_busy) return;

                // No usable input yet — a blank capture (player said nothing) OR an STT session error (the
                // socket idle-closing while WAITING for the player, who hasn't pressed-to-talk). Either way
                // stay QUIET and re-listen; never nag "say that again" before the player has actually
                // spoken. The next capture reopens an idle-closed socket. Bail only if genuinely offline, or
                // after prolonged silence.
                if (string.IsNullOrWhiteSpace(transcript))
                {
                    if (transcript == null && IsConnectivityLost())
                    {
                        Debug.LogWarning($"{LogPrefix} Connectivity lost during haggle — ending negotiation.");
                        SetTerminal(NegotiationPhase.Declined);
                        return;
                    }
                    if (++silent > MaxSilentRetries)
                    {
                        Debug.LogWarning($"{LogPrefix} No player speech after {silent} retries — ending negotiation.");
                        SetTerminal(NegotiationPhase.Declined);
                        return;
                    }
                    continue;
                }
                silent = 0;

                if (!await Bt_ProcessTurnAsync())            // LLM glitch — recover, then re-listen
                {
                    if (!_busy) return;
                    Bt_RecoverTurn();
                    if (IsTerminal) return;
                    if (!await SpeakPendingAsync()) return;
                    continue;
                }
                if (!_busy) return;

                Bt_ApplyResult();                            // queues the reply line; may go terminal / move stage
                if (!await SpeakPendingAsync()) return;
            }
        }

        // Speaks the line queued by the last Bt_* step (its subtitle was already shown by that step) and
        // awaits the TTS. Returns false if the negotiation ended while speaking.
        private async Task<bool> SpeakPendingAsync()
        {
            if (!_busy) return false;
            string line = _pendingLine;
            _pendingLine = null;
            if (!string.IsNullOrEmpty(line)) await SpeakAsync(line);
            else FlushPendingOffer();   // nothing to voice — don't strand an armed offer
            return _busy;
        }

        // --- Opening (point 2 & 3) -------------------------------------------------

        private async void RunOpeningFlow(MerchantNPC merchant, FishData fish)
        {
            try
            {
                // POINT 2: deterministic opening offer + deception roll; the LLM only PHRASES it.
                // The SAME roll fixes the whole negotiation envelope: opening offer, hard ceiling
                // (maxOffer) and the tone used on every later turn.
                bool isDeceptive = _calculator.RollDeception(merchant.Config);
                float opening = Mathf.Round(_calculator.GenerateOpeningOffer(merchant.Config, fish, isDeceptive));
                _state.currentOffer = opening;
                _state.offerHistory.Add(opening);
                _state.isDeceptive = isDeceptive;
                _state.maxOffer = Mathf.Round(_calculator.GenerateMaxOffer(merchant.Config, fish, isDeceptive, opening));
                _state.maxRaisePerTurn = _calculator.GenerateMaxRaiseStep(opening, _state.maxOffer);
                Debug.Log($"{LogPrefix} Opening offer {opening:0} EGP (ceiling {_state.maxOffer:0}, " +
                          $"raise step ≤{_state.maxRaisePerTurn:0}) for {fish.fishType} (deceptive={isDeceptive}).");

                if (_negotiation == null)
                {
                    Debug.LogWarning($"{LogPrefix} LLM unavailable; cannot phrase opening — scripted fallback.");
                    TriggerScriptedFallback(merchant, fish);
                    return;
                }

                MerchantTurnResult opener =
                    await _negotiation.GenerateOpeningLine(_state, isDeceptive, DecideOpeningGreeting(merchant));

                if (!_busy) return; // negotiation ended while awaiting

                if (opener == null)
                {
                    Debug.LogWarning($"{LogPrefix} Opening line failed — scripted fallback.");
                    TriggerScriptedFallback(merchant, fish);
                    return;
                }

                _state.state = NegotiationPhase.Negotiating;

                // POINT 3: subtitle immediately; audio plays in parallel; listen once it finishes.
                ShowSubtitle(opener.SpeechTranslationEnglish, opener.MerchantSpeech);
                PublishOffer();   // the opening offer was just voiced — log it on the summary card
                PlayMerchantLine(opener.MerchantSpeech, BeginListening);
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} Unexpected error in opening flow: {e}");
                if (_busy) TriggerScriptedFallback(merchant, fish);
            }
        }

        // --- Listening (point 4) ---------------------------------------------------

        private void BeginListening()
        {
            if (!_busy) return;

            EnsureVoiceCapture();
            if (_voiceCapture == null)
            {
                Debug.LogWarning($"{LogPrefix} No voice capture available; cannot listen — scripted fallback.");
                ConnectivityLossRecovery();
                return;
            }

            Debug.Log($"{LogPrefix} Listening for player response...");
            _voiceCapture.BeginCapture();   // turn-based: captures one utterance, then stops
        }

        // --- Player turn (point 5) -------------------------------------------------

        private async void HandleTranscriptionComplete(string text)
        {
            if (!_busy) return;

            _lastTranscript = text ?? "";
            ClearPlayerPartial();   // the final arrived — the live caption's job is done

            // Scale-flow phases await one utterance via ListenAsync — complete that await and skip the
            // flag/legacy paths below (the awaiting phase drives what happens next).
            if (_listenTcs != null)
            {
                TaskCompletionSource<string> tcs = _listenTcs;
                _listenTcs = null;
                Debug.Log($"{LogPrefix} Player said: \"{_lastTranscript}\"");
                tcs.TrySetResult(_lastTranscript);
                return;
            }

            // BT path: just surface the transcript; the ListenForPlayer node polls TranscriptReady and
            // the ProcessPlayerTurn node runs the LLM. The legacy async flow below is skipped.
            if (UseBehaviorGraph)
            {
                Debug.Log($"{LogPrefix} Player said: \"{_lastTranscript}\"");
                _transcriptReady = true;
                return;
            }

            // Blank / no-speech capture (mic committing silence, or the tail of the merchant's own TTS
            // bleeding into the mic). The player has NOT said anything, so stay SILENT and just keep
            // listening — the merchant must never speak before the player actually says something.
            if (string.IsNullOrWhiteSpace(text))
            {
                WaitSilentlyForPlayer("empty transcript");
                return;
            }
            _silentRetries = 0;   // real speech arrived — reset the silent-wait counter

            try
            {
                Debug.Log($"{LogPrefix} Player said: \"{text}\"");
                ShowThinkingCue();

                if (_negotiation == null)
                {
                    Debug.LogWarning($"{LogPrefix} LLM client missing during turn — scripted fallback.");
                    HideThinkingCue();
                    ConnectivityLossRecovery();
                    return;
                }

                // Tone follows the OPENING roll for the whole negotiation, matching the fixed
                // offer envelope — not re-rolled per turn.
                MerchantTurnResult result =
                    await _negotiation.ProcessPlayerTurn(_state, text, _state.isDeceptive);

                HideThinkingCue();

                if (!_busy) return; // ended while awaiting

                if (result == null)
                {
                    // POINT 6 vs POINT 5: connectivity loss => scripted fallback; transient => retry line.
                    if (IsConnectivityLost())
                    {
                        Debug.LogWarning($"{LogPrefix} LLM failed and connectivity lost — scripted fallback.");
                        ConnectivityLossRecovery();
                    }
                    else
                    {
                        Debug.LogWarning($"{LogPrefix} LLM failed (recoverable) — fallback line + re-listen.");
                        RecoverWithFallbackLine();
                    }
                    return;
                }

                _consecutiveRecoveries = 0; // a clean turn resets the recovery counter
                ApplyTurnResult(result);
            }
            catch (Exception e)
            {
                HideThinkingCue();
                Debug.LogError($"{LogPrefix} Unexpected error processing player turn: {e}");
                if (_busy) ConnectivityLossRecovery(); // never leave the player frozen
            }
        }

        private void HandleTranscriptionFailed(string reason)
        {
            if (!_busy) return;

            Debug.LogWarning($"{LogPrefix} STT failed: {reason}");

            // Scale-flow phases await via ListenAsync — resolve that await with a null transcript so the
            // phase re-listens on its next loop instead of hanging on the failed capture.
            if (_listenTcs != null)
            {
                TaskCompletionSource<string> tcs = _listenTcs;
                _listenTcs = null;
                tcs.TrySetResult(null);
                return;
            }

            // BT path: flag the failure; the ListenForPlayer node reacts (recovery/scripted fallback
            // are graph branches). The legacy recovery below is skipped.
            if (UseBehaviorGraph)
            {
                _captureFailed = true;
                return;
            }

            if (IsConnectivityLost())
            {
                ConnectivityLossRecovery();   // point 6: genuinely offline / consent lost
                return;
            }

            // Transient capture glitch while WAITING for the player (STT timeout, session blip, TTS
            // echo). Do NOT speak — stay silent and re-listen. The merchant only ever talks in
            // response to actual player speech, never a "say that again" before they've spoken.
            WaitSilentlyForPlayer(reason);
        }

        // Applies the LLM's action to game state IMMEDIATELY, then plays the merchant's line in
        // parallel (point 5). Non-terminal actions re-open the mic after the line; terminal actions
        // resolve after the line so the closing/farewell is heard before the coordinator moves on.
        // Hard ceiling on how long one sale may drag on, protecting the LLM/TTS usage budget. This is
        // independent of the LLM layer's own hold-at-max patience ladder, which usually ends a sale
        // long before this — the limit exists for the player who negotiates forever.
        //
        // It mirrors that ladder's invariant (see the WALK-AWAY WARNING GATE in MerchantNegotiationLLM):
        // the merchant WARNS on the second-to-last turn and only LEAVES on the last, so it never
        // vanishes mid-conversation with no signal. That gate lives in the LLM validation layer and has
        // already run by the time we get here, so this path has to honour the rule itself.
        //
        // Overrides the ACTION rather than ending the negotiation directly, so both callers route it
        // through their own existing WalkAway/HoldFirm cases instead of duplicating terminal handling.
        // Called from ApplyTurnResult AND Bt_ApplyResult — the two are near-identical twins that have
        // drifted before, so the limit lives in one place by construction.
        private void ApplyTurnLimit(MerchantTurnResult result)
        {
            if (_maxPlayerTurns <= 0 || _state == null) return;

            // Never override a turn that is already closing the sale: if the player finally accepts (or
            // confirms they are done) on the very last turn, that outcome stands.
            if (result.Action == MerchantAction.Accept || result.Action == MerchantAction.PlayerDeclined)
                return;

            if (_state.turnCount >= _maxPlayerTurns)
            {
                Debug.Log($"{LogPrefix} Turn limit ({_maxPlayerTurns}) reached — merchant walks away.");
                result.Action = MerchantAction.WalkAway;
                result.MerchantSpeech = MerchantNegotiationLLM.CoercedWalkAwayArabic;
                result.SpeechTranslationEnglish = MerchantNegotiationLLM.CoercedWalkAwayEnglish;
                return;
            }

            // Second-to-last turn: spend it on the warning, unless the patience ladder already warned.
            if (_state.turnCount >= _maxPlayerTurns - 1 && !_state.walkAwayWarningGiven)
            {
                Debug.Log($"{LogPrefix} Turn {_state.turnCount}/{_maxPlayerTurns} — final warning before " +
                          "the turn limit walks the merchant away.");
                result.Action = MerchantAction.HoldFirm;
                result.IsFinalWarning = true;
                result.MerchantSpeech = MerchantNegotiationLLM.CoercedFinalWarningArabic;
                result.SpeechTranslationEnglish = MerchantNegotiationLLM.CoercedFinalWarningEnglish;
                _state.walkAwayWarningGiven = true;
            }
        }

        private void ApplyTurnResult(MerchantTurnResult result)
        {
            _state.turnCount++;
            ApplyTurnLimit(result);
            Debug.Log($"{LogPrefix} Turn {_state.turnCount}: action={result.Action}" +
                      (result.HasNewOffer ? $", newOffer={result.NewOfferAmount:0.#}" : "."));

            // Subtitle shows immediately — never wait on audio (points 3 & 5).
            ShowSubtitle(result.SpeechTranslationEnglish, result.MerchantSpeech);

            switch (result.Action)
            {
                case MerchantAction.RaiseOffer:
                    // Ratchet: the offer can only ever go UP. Guard against any lower value slipping
                    // through so a raise never silently reduces the standing offer.
                    _state.currentOffer = Mathf.Max(_state.currentOffer, result.NewOfferAmount);
                    _state.offerHistory.Add(_state.currentOffer);
                    _state.state = NegotiationPhase.Negotiating;
                    PlayMerchantLine(result.MerchantSpeech, BeginListening);
                    break;

                case MerchantAction.HoldFirm:
                case MerchantAction.ClarifyRequest:
                    // Holding firm while ALREADY at the ceiling burns the merchant's hold-at-max
                    // patience; once spent, the LLM layer forces the next turn to close the deal.
                    if (result.Action == MerchantAction.HoldFirm
                        && _state.maxOffer > 0f && _state.currentOffer >= _state.maxOffer)
                        _state.turnsHeldAtMax++;
                    _state.state = NegotiationPhase.Negotiating;
                    PlayMerchantLine(result.MerchantSpeech, BeginListening);
                    break;

                case MerchantAction.ConfirmDecline:
                    // The player said they no longer want to sell. Nothing is applied yet — the merchant
                    // asks them to confirm and we listen for the answer. NegotiationState.awaitingDecline-
                    // Confirmation (set by the LLM layer) lets the NEXT turn resolve as PlayerDeclined.
                    Debug.Log($"{LogPrefix} Player wants to call the sale off — asking them to confirm.");
                    _state.state = NegotiationPhase.Negotiating;
                    PlayMerchantLine(result.MerchantSpeech, BeginListening);
                    break;

                case MerchantAction.Accept:
                    _state.currentOffer = result.NewOfferAmount;
                    _state.offerHistory.Add(_state.currentOffer);
                    _state.state = NegotiationPhase.Accepted;
                    PlayMerchantLine(result.MerchantSpeech, () => EndWithStat(NegotiationPhase.Accepted));
                    break;

                case MerchantAction.WalkAway:
                    _state.state = NegotiationPhase.WalkedAway;
                    PlayMerchantLine(result.MerchantSpeech, () => EndWithStat(NegotiationPhase.WalkedAway));
                    break;

                case MerchantAction.PlayerDeclined:
                    // The player CONFIRMED they don't want to sell (the LLM layer never lets this action
                    // through without a preceding ConfirmDecline turn). The rejection is applied right
                    // here — the farewell line just plays out over an already-Declined negotiation.
                    Debug.Log($"{LogPrefix} Player confirmed the rejection — ending the sale.");
                    _state.state = NegotiationPhase.Declined;
                    PlayMerchantLine(result.MerchantSpeech, () => EndWithStat(NegotiationPhase.Declined));
                    break;

                case MerchantAction.ConfirmAccept:
                    // The acceptance mirror of ConfirmDecline. The player agreed to sell; nothing is applied
                    // yet — the merchant asks them to confirm and we listen for the answer.
                    // NegotiationState.awaitingAcceptConfirmation (set by the LLM layer) lets the NEXT turn
                    // resolve as PlayerAccepted.
                    Debug.Log($"{LogPrefix} Player wants to accept — asking them to confirm the sale.");
                    _state.state = NegotiationPhase.Negotiating;
                    PlayMerchantLine(result.MerchantSpeech, BeginListening);
                    break;

                case MerchantAction.PlayerAccepted:
                    // The player CONFIRMED they agree to sell (the LLM layer never lets this action through
                    // without a preceding ConfirmAccept turn). The sale closes at the agreed price carried
                    // on the result; the closing line plays out over an already-Accepted negotiation.
                    Debug.Log($"{LogPrefix} Player confirmed the sale — closing the deal.");
                    _state.currentOffer = result.NewOfferAmount;
                    _state.offerHistory.Add(_state.currentOffer);
                    _state.state = NegotiationPhase.Accepted;
                    PlayMerchantLine(result.MerchantSpeech, () => EndWithStat(NegotiationPhase.Accepted));
                    break;
            }

            // Surface the number to the market ticket only when the merchant actually voiced one.
            if (result.HasNewOffer || result.Action == MerchantAction.PlayerAccepted) PublishOffer();
        }

        /// <summary>
        /// Aborts the negotiation with <paramref name="merchant"/> (player left the conversation area).
        /// Stops any line still playing, then tears down as a walk-away — which fires
        /// <c>merchant.EndNegotiation</c> so the coordinator releases its lock and the merchant walks home.
        /// No-op unless this merchant is the one currently negotiating.
        /// </summary>
        public void CancelNegotiation(MerchantNPC merchant)
        {
            if (merchant == null || merchant != _activeMerchant || !_busy) return;

            Debug.Log($"{LogPrefix} Negotiation with '{NameOf(merchant)}' cancelled — player left the conversation area.");

            // If a scripted (offline) sale is running it owns the flow — cancel it and let its onEnded
            // callback drive Cleanup. Cleanup is idempotent, so the fall-through below is a safe no-op.
            if (_scriptedSale != null && _scriptedSale.IsRunning)
                _scriptedSale.Cancel(merchant);

            // Silence any merchant line still playing so it doesn't keep talking to an empty spot.
            if (_activeAudioSource != null) _activeAudioSource.Stop();

            // For the Behavior-graph path, mark terminal so any polling nodes unwind; Cleanup is
            // idempotent (keyed on _activeMerchant), so a later Bt_Resolve becomes a no-op.
            SetTerminal(NegotiationPhase.WalkedAway);
            Cleanup(NegotiationPhase.WalkedAway);
        }

        // --- Recovery paths --------------------------------------------------------

        // Point 5: a recoverable STT/LLM glitch (still online). Speak a generic "say that again"
        // line and re-listen — but give up to the scripted fallback if it keeps failing.
        private void RecoverWithFallbackLine()
        {
            if (!_busy) return;

            _consecutiveRecoveries++;
            if (_consecutiveRecoveries > MaxConsecutiveRecoveries)
            {
                Debug.LogWarning($"{LogPrefix} {_consecutiveRecoveries} consecutive failures — " +
                                 "giving up on AI flow, scripted fallback.");
                ConnectivityLossRecovery();
                return;
            }

            ShowSubtitle(FallbackEnglish, FallbackArabic);
            PlayMerchantLine(FallbackArabic, BeginListening);
        }

        // While WAITING for the player (blank capture or transient STT glitch): stay SILENT and
        // re-listen after a short backoff, so the merchant never prompts before the player has spoken.
        // The backoff avoids tight-looping on a hard failure; after too many silent retries we quietly
        // hand off (scripted fallback) — never a spoken line.
        private void WaitSilentlyForPlayer(string why)
        {
            if (!_busy) return;

            _silentRetries++;
            if (_silentRetries > MaxSilentRetries)
            {
                Debug.LogWarning($"{LogPrefix} Still no player speech after {_silentRetries} silent retries " +
                                 $"({why}) — quiet scripted fallback.");
                ConnectivityLossRecovery();
                return;
            }

            Debug.Log($"{LogPrefix} No player speech yet ({why}); staying silent, re-listening ({_silentRetries}).");
            CancelInvoke(nameof(ReListen));
            Invoke(nameof(ReListen), 0.4f);
        }

        // Invoke target (Invoke needs a parameterless instance method).
        private void ReListen() => BeginListening();

        // Point 6: connectivity/voice lost mid-negotiation. End the AI flow and hand to the scripted
        // fallback so the player is never stuck waiting on a dead connection.
        private void ConnectivityLossRecovery()
        {
            if (_activeMerchant == null) return;

            Debug.LogWarning($"{LogPrefix} Connectivity/voice lost — ending AI flow, scripted fallback.");
            MerchantNPC merchant = _activeMerchant;
            FishData fish = _state != null ? _state.fishBeingSold : null;
            TriggerScriptedFallback(merchant, fish);
        }

        // Voice is unavailable at a decision point. Decide HOW to fall back: if the player actually wants
        // voice (granted the in-app mic consent) but the OS microphone permission is the missing piece,
        // surface a readable in-VR notice pointing them at the headset settings before handing off. For an
        // outright decline or a dropped connection there is nothing actionable to show — fall back quietly.
        private void FallbackAfterVoiceUnavailable(MerchantNPC merchant, FishData fish)
        {
            ConsentState consent = _consent != null ? _consent.GetConsentStatus() : default;
            if (consent.MicConsent == ConsentStatus.Granted && !MicrophonePermission.IsGranted)
            {
                ShowMicPermissionNoticeThenFallback(merchant, fish);
                return;
            }
            TriggerScriptedFallback(merchant, fish);
        }

        // Shows the enable-mic notice on the merchant's subtitle (via ShowSubtitle -> MerchantSpokeEvent),
        // holds it for MicNoticeSeconds so it is readable, then hands to the scripted fallback. The delay
        // matters because Cleanup publishes MerchantStoppedSpeakingEvent, which clears the subtitle.
        private void ShowMicPermissionNoticeThenFallback(MerchantNPC merchant, FishData fish)
        {
            Debug.LogWarning($"{LogPrefix} Voice consent granted but OS mic permission missing — " +
                             $"showing the enable-mic notice for '{NameOf(merchant)}'.");
            ShowSubtitle(MicPermissionNoticeEnglish, MicPermissionNoticeArabic);
            _pendingFallbackFish = fish;
            CancelInvoke(nameof(DeferredScriptedFallback));
            Invoke(nameof(DeferredScriptedFallback), MicNoticeSeconds);
        }

        // Invoke target: fires the scripted fallback once the mic notice has been shown long enough. A
        // teardown in the meantime clears _activeMerchant, making this a no-op.
        private void DeferredScriptedFallback()
        {
            if (_activeMerchant == null) return;
            FishData fish = _pendingFallbackFish;
            _pendingFallbackFish = null;
            TriggerScriptedFallback(_activeMerchant, fish);
        }

        // Hands the sale to the SCRIPTED (offline, button-based) flow when the AI voice path is unavailable
        // or force-scripted. The scripted service drives its own catch → quote → accept/decline loop and
        // reports the outcome back through the onEnded callback, which routes to Cleanup — the same teardown
        // the AI path uses. Falls back to ending cleanly only when no scripted service can run.
        private void TriggerScriptedFallback(MerchantNPC merchant, FishData fish)
        {
            IScriptedSaleService scripted = ResolveScriptedSale();
            if (scripted != null && merchant != null && merchant.SellScale != null)
            {
                // Drop the AI voice capture / event subscriptions before the scripted flow takes over so a
                // late transcript can't drive an already-handed-off negotiation. The scripted controller
                // manages its own subscriptions.
                Unsubscribe();
                _voiceCapture?.CloseSession();

                // Own the negotiation while the scripted sale runs. Some entry paths reach here with _busy
                // still false (voice-unavailable gate, no-consent), and CancelNegotiation early-returns on
                // !_busy — so without this, walking away mid-scripted-sale would silently do nothing.
                _busy = true;

                Debug.Log($"{LogPrefix} Handing '{NameOf(merchant)}' to the scripted (offline) sale.");
                if (scripted.BeginScriptedSale(merchant, phase => Cleanup(phase)))
                    return;
            }

            // No scripted service (or the merchant has no scale) — end cleanly so the player isn't stuck.
            string fishName = fish != null ? fish.fishType : "<no fish>";
            Debug.LogWarning($"{LogPrefix} Scripted fallback unavailable for '{NameOf(merchant)}' / {fishName} " +
                             "— ending cleanly.");
            Cleanup(NegotiationPhase.Declined);
        }

        // Resolves the scripted-sale service: the serialized reference, else a ScriptedSaleController in the
        // scene. Cached (including a null result) so the scene is searched at most once.
        private IScriptedSaleService ResolveScriptedSale()
        {
            if (_scriptedSaleResolved) return _scriptedSale;
            _scriptedSaleResolved = true;

            if (_scriptedSaleSource is IScriptedSaleService fromField)
            {
                _scriptedSale = fromField;
                return _scriptedSale;
            }

            _scriptedSale = FindFirstObjectByType<ScriptedSaleController>();
            if (_scriptedSale == null)
                Debug.LogWarning($"{LogPrefix} No ScriptedSaleController in the scene — the offline fallback " +
                                 "will end negotiations cleanly instead of running a scripted sale.");
            return _scriptedSale;
        }

        // --- Resolution (point 7) --------------------------------------------------

        private void EndWithStat(NegotiationPhase phase)
        {
            if (_activeMerchant == null) return; // already resolved

            // Scale-based bundle sale: the sale-confirm phase already recorded each fish and removed it
            // from the scale in PayoutBundle, so there is nothing to record here — just tear down.
            if (IsBundleSale)
            {
                Debug.Log($"{LogPrefix} Resolved bundle sale as {phase}.");
                Cleanup(phase);
                return;
            }

            FishData fish = _state != null ? _state.fishBeingSold : null;
            float fair = fish != null ? fish.fairMarketValue : 0f;
            float finalPrice = _state != null ? _state.currentOffer : 0f;

            // A sale only completes on Accept. Capture WHICH fish (type + weight) sold and for HOW MUCH,
            // then hand it to PlayerStats (which persists it and fires OnSaleRecorded) and tell the
            // inventory to consume that fish — the fish-sell process + inventory-validation seam.
            if (phase == NegotiationPhase.Accepted && fish != null)
            {
                var record = new FishSaleRecord
                {
                    fishType = fish.fishType,
                    fishTypeArabic = fish.fishTypeArabic,
                    weight = fish.weight,
                    agreedPrice = Mathf.Round(finalPrice),
                    fairMarketValue = fair,
                    successRate = _calculator.CalculateSuccessRate(finalPrice, fair)
                };

                if (_playerStats != null)
                    _playerStats.RecordSale(record);
                else
                    Debug.LogWarning($"{LogPrefix} PlayerStats missing; sale of {record.fishType} not recorded.");

                // One selling-score notification for this (single-fish) sale.
                _eventService?.Publish(new SellScoreEvent(record.successRate, record.agreedPrice, 1));

                // Remove/consume the sold fish from the inventory (no-op for the random test provider).
                ResolveFishInventory()?.MarkFishSold(fish);

                Debug.Log($"{LogPrefix} SOLD {fish.fishType} ({fish.fishTypeArabic}) {fish.weight:0.##}kg for " +
                          $"{record.agreedPrice:0} EGP — success {record.successRate:0.#}.");
            }
            else
            {
                Debug.Log($"{LogPrefix} Resolved {phase} — no sale (last talked price {finalPrice:0.#}).");
            }

            Cleanup(phase);
        }

        // Idempotent teardown (keyed on _activeMerchant). Notifies the coordinator so it releases its
        // lock and starts the cooldown for the next approach (point 7).
        private void Cleanup(NegotiationPhase phase)
        {
            MerchantNPC merchant = _activeMerchant;
            if (merchant == null) return; // already cleaned up

            CancelInvoke(nameof(ReListen));               // drop any pending silent re-listen
            CancelInvoke(nameof(DeferredScriptedFallback)); // and any pending mic-notice fallback
            _pendingFallbackFish = null;

            // Unblock any scale-flow phase awaiting a listen/speak so it unwinds instead of hanging
            // (its next `if (!_busy) return false;` exits the phase cleanly). _busy is cleared below.
            _busy = false;
            _listenTcs?.TrySetResult(null);
            _listenTcs = null;
            _speakTcs?.TrySetResult(true);
            _speakTcs = null;
            _placementSignal?.TrySetResult(false);
            _placementSignal = null;
            _pendingFumble = null;

            Unsubscribe();
            _voiceCapture?.CloseSession();

            // Published before _activeMerchant is cleared below, so listeners still know WHO stopped.
            _eventService?.Publish(new MerchantStoppedSpeakingEvent(merchant));

            _activeMerchant = null;
            _state = null;
            _activeAudioSource = null;
            _busy = false;

            Debug.Log($"{LogPrefix} Cleanup ({phase}); notifying coordinator.");
            merchant.EndNegotiation(phase); // -> MerchantApproachCoordinator.HandleNegotiationEnded (lock + cooldown)
        }

        // --- External call wrappers ------------------------------------------------

        // Plays a merchant line via TTS. onDone fires when audio finishes OR fails, so the flow
        // never stalls on audio — the subtitle is already on screen (point 6).
        private void PlayMerchantLine(string arabicText, Action onDone)
        {
            // Any offer armed for this line lands as its speech ends (see PublishOffer / FlushPendingOffer),
            // so the summary card inks the price the moment the merchant stops voicing it.
            Action done = () => { FlushPendingOffer(); onDone?.Invoke(); };

            if (_tts == null)
            {
                Debug.LogWarning($"{LogPrefix} TTS manager unavailable; continuing on subtitle only.");
                done();
                return;
            }

            // Voice is the one assigned to THIS NPC at spawn (stable across the whole negotiation),
            // not a per-archetype/per-call value. The voice PARAMETERS (speed/stability/similarity/style)
            // come from this merchant's archetype config, so each merchant sounds distinct.
            string voiceId = _activeMerchant != null ? _activeMerchant.AssignedVoiceId : null;
            MerchantConfig config = _activeMerchant != null ? _activeMerchant.Config : null;
            TtsVoiceSettings? voiceSettings = config != null ? config.VoiceSettings : null;

            // A missing voice ID means the merchant's config has no VoiceId — the request would fail
            // silently (subtitle only). Surface it loudly so a mis-authored config is obvious instead
            // of the merchant appearing to "lose" its voice mid-scene.
            if (string.IsNullOrWhiteSpace(voiceId))
                Debug.LogWarning($"{LogPrefix} Merchant '{NameOf(_activeMerchant)}' has no ElevenLabs " +
                                 "VoiceId on its MerchantConfig; TTS will fall back to subtitle only.");
            _tts.PlaySpeech(
                voiceId, arabicText, _activeAudioSource,
                onComplete: done,
                onError: () =>
                {
                    Debug.LogWarning($"{LogPrefix} TTS failed; continuing on subtitle only.");
                    done();
                },
                voiceSettings: voiceSettings);
        }

        // --- Behavior-graph steps (called by the custom nodes in Merchant/Behavior) ----------------
        //
        // Each Bt_* method is one node's payload. Async steps return a Task the node polls; TTS/STT
        // steps flip a flag (LineFinished / TranscriptReady / CaptureFailed) the node polls. The graph
        // owns the sequencing, the loop and the branches — these just do one step of work each.

        /// <summary>
        /// True while the HAGGLE loop should keep looping. Busy and not yet terminal — and, for a bundle
        /// sale, only while still in the Negotiating stage (agreeing a price moves it to
        /// AwaitingSaleConfirm, which must exit the haggle loop into the sale-confirm phase).
        /// </summary>
        public bool NegotiationOngoing =>
            _busy && !IsTerminal && (!IsBundleSale || _state.stage == NegotiationStage.Negotiating);

        private void ResetStepState()
        {
            IsTerminal = false;
            Outcome = NegotiationPhase.Declined;
            _pendingLine = null;
            _lineFinished = false;
            _transcriptReady = false;
            _captureFailed = false;
            _lastTranscript = "";
            _lastResult = null;
            _pendingFumble = null;
            _offerPending = false;
        }

        private void SetTerminal(NegotiationPhase phase)
        {
            IsTerminal = true;
            Outcome = phase;
        }

        /// <summary>Condition: is voice mode (mic + internet consent + connectivity) available?</summary>
        public bool Bt_ConsentAvailable() =>
            _consent != null && _consent.IsVoiceModeAvailable();

        /// <summary>
        /// Setup step: build the negotiation state and warm the STT session. When the merchant has a
        /// sell scale, sets up the SCALE-BASED bundle sale in the AwaitingPlacement stage (the catch is
        /// resolved later, from the scale, once the player loads it). Otherwise falls back to the legacy
        /// single-fish path (resolve one fish up front).
        /// </summary>
        public bool Bt_Setup()
        {
            if (_activeMerchant == null || _activeMerchant.Config == null) return false;

            if (_activeMerchant.SellScale != null)
            {
                _state = new NegotiationState
                {
                    currentMerchant = _activeMerchant.Config,
                    fishCatalog = ResolveFishCatalog(),
                    state = NegotiationPhase.Approaching,
                    stage = NegotiationStage.AwaitingPlacement,
                    turnCount = 0
                };
                _voiceCapture?.OpenSession();
                Debug.Log($"{LogPrefix} BT scale-sale setup for '{NameOf(_activeMerchant)}'.");
                return true;
            }

            // Legacy single-fish path (no scale wired): resolve one fish up front.
            FishData fish = ResolveFishToSell();
            if (fish == null)
            {
                Debug.LogWarning($"{LogPrefix} No fish to sell; BT setup failed for '{NameOf(_activeMerchant)}'.");
                return false;
            }

            _state = new NegotiationState
            {
                currentMerchant = _activeMerchant.Config,
                fishBeingSold = fish,
                fishCatalog = ResolveFishCatalog(),
                state = NegotiationPhase.Approaching,
                turnCount = 0
            };
            _voiceCapture?.OpenSession();
            Debug.Log($"{LogPrefix} BT setup for '{NameOf(_activeMerchant)}' over {fish.fishType}.");
            return true;
        }

        /// <summary>
        /// Opening step. Bundle sale: the envelope was already computed when the catch was snapshotted
        /// from the scale, so this just phrases the ACKNOWLEDGE + opening-total line. Legacy single fish:
        /// roll deception, compute the envelope, then phrase the opening.
        /// </summary>
        public async Task<bool> Bt_RunOpeningAsync()
        {
            try
            {
                if (_state == null || _negotiation == null) return false;

                if (IsBundleSale)
                {
                    // The welcome line already greeted; the acknowledge line never greets again.
                    MerchantTurnResult ack = await _negotiation.GenerateOpeningLine(_state, _state.isDeceptive, withGreeting: false);
                    if (!_busy || ack == null) return false;

                    ShowSubtitle(ack.SpeechTranslationEnglish, ack.MerchantSpeech);
                    PublishOffer();   // the opening bundle total was just voiced — log it on the summary card
                    _pendingLine = ack.MerchantSpeech;
                    return true;
                }

                bool isDeceptive = _calculator.RollDeception(_activeMerchant.Config);
                float opening = Mathf.Round(_calculator.GenerateOpeningOffer(_activeMerchant.Config, _state.fishBeingSold, isDeceptive));
                _state.currentOffer = opening;
                _state.offerHistory.Add(opening);
                _state.isDeceptive = isDeceptive;
                _state.maxOffer = Mathf.Round(_calculator.GenerateMaxOffer(_activeMerchant.Config, _state.fishBeingSold, isDeceptive, opening));
                _state.maxRaisePerTurn = _calculator.GenerateMaxRaiseStep(opening, _state.maxOffer);
                Debug.Log($"{LogPrefix} Opening offer {opening:0} EGP (ceiling {_state.maxOffer:0}, " +
                          $"raise step ≤{_state.maxRaisePerTurn:0}) for {_state.fishBeingSold.fishType} (deceptive={isDeceptive}).");

                MerchantTurnResult opener = await _negotiation.GenerateOpeningLine(
                    _state, isDeceptive, DecideOpeningGreeting(_activeMerchant));
                if (!_busy || opener == null) return false;

                _state.state = NegotiationPhase.Negotiating;
                ShowSubtitle(opener.SpeechTranslationEnglish, opener.MerchantSpeech);
                PublishOffer();   // the opening offer was just voiced — log it on the summary card
                _pendingLine = opener.MerchantSpeech;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} BT opening step error: {e}");
                return false;
            }
        }

        // --- Scale-flow phases (coarse async BT nodes) ----------------------------
        //
        // The placement and sale-confirm phases are conversational LOOPS (welcome → wait for fish; read
        // summary → confirm → re-verify). Modeling those as fine-grained graph loops would be fragile to
        // author, so each is ONE async node that drives its own listen/speak turns via the bridges below.
        // The haggle in between stays the existing fine-grained graph loop.

        /// <summary>
        /// PLACEMENT phase: greet, ask the player to load the scale, and wait until they have. Reacts to
        /// fumbles (fish thrown at the merchant / dropped short of the scale) with a light-hearted line.
        /// On success the catch is snapshotted into a priced bundle and the stage moves to Negotiating.
        /// Returns false if the player never loads the scale (the graph then runs the scripted fallback).
        /// </summary>
        public async Task<bool> Bt_RunPlacementPhaseAsync()
        {
            try
            {
                ISellScale scale = _activeMerchant != null ? _activeMerchant.SellScale : null;
                if (_state == null || _negotiation == null || scale == null) return false;

                // A bit of folk wisdom before business, from the good-natured traders only and only
                // sometimes. When it fires it IS the opening — idiom, then its own "…anyway, put the fish
                // on the scale" follow-up — and the LLM welcome is skipped rather than stacked on top of
                // it. Three opening speeches in a row is a monologue, and the pair already says everything
                // the welcome would. RegisterConversation still runs either way, so first-meeting state
                // advances whichever opening the player got.
                bool greet = DecideOpeningGreeting(_activeMerchant);

                IdiomOpening opening = await SpeakIdiomOpeningAsync();
                if (opening == IdiomOpening.Aborted) return false;
                if (opening == IdiomOpening.NotSpoken)
                {
                    // Welcome line (greets on the first conversation / sometimes after).
                    if (!await SpeakLineAsync(await _negotiation.GenerateWelcomeLine(_state, greet))) return false;
                }

                int prompts = 0;
                bool askAllPlaced = true;   // false for the one round after a "no" (a keep-placing line already asked)
                while (_busy && _state.stage == NegotiationStage.AwaitingPlacement)
                {
                    // React to a fumble the INSTANT it happens (HandleFishFumbled wakes this loop), rather
                    // than waiting for the player to next speak.
                    if (_pendingFumble.HasValue)
                    {
                        FishFumbleReason reason = _pendingFumble.Value;
                        _pendingFumble = null;
                        if (!await SpeakLineAsync(await _negotiation.GenerateFumbleLine(_state, reason))) return false;
                        continue;
                    }

                    // Scale still empty: wait for the player to load it (a scale change / fumble wakes us),
                    // nudging them if they stall too long, and giving up after too many silent nudges.
                    if (!scale.HasFish)
                    {
                        bool woke = await WaitForPlacementActivityAsync(_placementIdleSeconds);
                        if (!_busy) return false;
                        if (_pendingFumble.HasValue || scale.HasFish) continue; // handled at the top / below
                        if (woke) continue;                                     // some other change — re-evaluate

                        if (++prompts > _maxPlacementPrompts)
                        {
                            Debug.LogWarning($"{LogPrefix} Player never loaded the scale after {prompts} prompts — giving up.");
                            return false;
                        }
                        if (!await SpeakLineAsync(await _negotiation.GenerateWelcomeLine(_state, withGreeting: false))) return false;
                        continue;
                    }

                    // Scale HAS fish: do NOT price yet — confirm the fisherman has placed his WHOLE catch,
                    // so he can keep loading more before we snapshot the bundle. (Skipped for the one round right
                    // after a "no", where a keep-placing line already invited him — go straight to listening.)
                    if (askAllPlaced && !await SpeakLineAsync(await _negotiation.GenerateAllPlacedPromptLine(_state)))
                        return false;
                    askAllPlaced = true;

                    // Listen for the yes/no answer WITHOUT a premature timeout that could drop a late reply.
                    // Plain ListenAsync waits until the player speaks OR the STT socket idle-closes; on a
                    // drop/blank we RE-ARM (the next capture reopens the socket) and keep waiting, up to
                    // _maxAnswerListens attempts, so the player's real answer is captured, not swallowed.
                    ConfirmationAnswer answered = ConfirmationAnswer.Unclear;
                    for (int listenTry = 0; listenTry < _maxAnswerListens && _busy; listenTry++)
                    {
                        string text = await ListenAsync();
                        if (!_busy) return false;
                        if (_pendingFumble.HasValue || !scale.HasFish) break;  // handled after the loop
                        if (string.IsNullOrWhiteSpace(text))                    // drop / idle-close / blank — re-arm
                        {
                            Debug.Log($"{LogPrefix} Placement confirm: no usable answer (attempt {listenTry + 1}/{_maxAnswerListens}) — re-listening.");
                            continue;
                        }
                        answered = await _negotiation.InterpretConfirmation(text, MerchantPhase.PlacementConfirm);
                        Debug.Log($"{LogPrefix} Placement confirm: heard \"{text}\" → classified {answered}.");
                        break;
                    }
                    if (!_busy) return false;
                    if (_pendingFumble.HasValue) continue;   // fumbled while answering — quip first
                    if (!scale.HasFish) continue;            // took everything back off — back to the empty branch

                    if (answered == ConfirmationAnswer.Yes)
                    {
                        SnapshotBundleFromScale(scale);      // -> fills the bundle + price envelope, stage=Negotiating
                        return true;
                    }

                    // Not "yes": give up after too many rounds; otherwise KEEP THE CONVERSATION ALIVE instead of
                    // the old silent wait that left the mic off ~15s. A clear "no, more to add" gets a warm
                    // "take your time" line and we listen again (he was asked to say when he's set); an Unclear /
                    // mis-heard reply simply re-asks the all-placed question and listens again — never dead air.
                    if (++prompts > _maxPlacementPrompts)
                    {
                        Debug.LogWarning($"{LogPrefix} No 'that's all my fish' confirmation after {prompts} rounds — giving up.");
                        return false;
                    }
                    if (answered == ConfirmationAnswer.No)
                    {
                        if (!await SpeakLineAsync(await _negotiation.GenerateKeepPlacingLine(_state))) return false;
                        askAllPlaced = false;   // the keep-placing line already prompted him — listen, don't re-ask
                    }
                    // else Unclear/silent: fall through — the loop re-asks the all-placed prompt and re-listens.
                }

                return _state.stage == NegotiationStage.Negotiating;
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} BT placement phase error: {e}");
                return false;
            }
        }

        /// <summary>
        /// SALE-CONFIRM phase (after the player accepts a bundle price): an INTERACTIVE review of the deal.
        /// The merchant reads back the per-fish summary + total and invites the player to confirm, keep
        /// haggling, or change the catch. Each round it folds fish the player added / took off the scale into
        /// the running total (an added fish extends it by its fair value, a removed one drops its share) and,
        /// when the catch changed, ACKNOWLEDGES what changed by name and re-reads the updated summary before
        /// treating the player's words as anything else. Speech that isn't a fish change runs through the full
        /// haggle turn, so the player can still renegotiate the total; a clear agreement SEALS the deal (pay
        /// out + spoken closing line, Accepted), a clear walk-away ends it Declined. An emptied scale nudges
        /// for re-placement instead of cancelling. Always resolves to a terminal state. This is where the
        /// bundle is finally AGREED — the haggle acceptance only routes here (see Bt_ApplyResult). When
        /// <see cref="_enableFishSummary"/> is OFF the whole review is skipped: acceptance pays out and speaks
        /// the closing line immediately (accept > close).
        /// </summary>
        public async Task<bool> Bt_RunSaleConfirmPhaseAsync()
        {
            try
            {
                ISellScale scale = _activeMerchant != null ? _activeMerchant.SellScale : null;
                if (_state == null || _negotiation == null || scale == null) return false;

                // Summary disabled: the player's acceptance already sealed the price, so skip the per-fish
                // read-back and its confirmation entirely. Split the accepted total across the accepted catch,
                // drop any fish taken back off (its share just falls away), then pay out and close (accept > close).
                if (!_enableFishSummary)
                {
                    Dictionary<FishData, float> frozen = BuildBundlePriceMap();
                    ReconcileBundleWithScale(scale);
                    if (_state.fishBundle.Count == 0) { SetTerminal(NegotiationPhase.Declined); return true; }
                    await SealBundleSaleAsync(scale, frozen);
                    return true;
                }

                // Interactive summary. Price EVERYTHING off a single NEGOTIATION RATE = the agreed offer per
                // unit of fair value (each fish's price = its fairMarketValue × rate, exactly how the total is
                // distributed). A fish's price then depends ONLY on the fish and the current rate — never on the
                // order fish were added/removed — so removing a fish and adding the same one back returns to the
                // IDENTICAL total. The rate moves only when the player haggles; the ceiling rate is fixed.
                float fairSum = SumFairValue(_state.fishBundle);
                float rate = fairSum > 0f ? _state.currentOffer / fairSum : 1f;
                float maxRate = fairSum > 0f ? Mathf.Max(rate, _state.maxOffer / fairSum) : rate;

                var added = new List<FishData>();
                var removed = new List<FishData>();
                ReconcileScaleForSummary(scale, rate, maxRate, added, removed);   // fold any drift since acceptance

                bool speakSummary = true;   // read the summary now, and again whenever the catch changes
                int emptyNudges = 0;
                while (_busy)
                {
                    if (_state.fishBundle.Count == 0)
                    {
                        // Empty scale — do NOT cancel. Lifting one fish off a small scale can knock its
                        // neighbours out of the trigger, and the player may just be rearranging. Nudge them to
                        // put fish back and wait (same patience as placement); give up only after many rounds.
                        if (++emptyNudges > _maxPlacementPrompts)
                        {
                            Debug.Log($"{LogPrefix} Scale stayed empty through the summary — ending the sale.");
                            SetTerminal(NegotiationPhase.Declined);
                            return true;
                        }
                        if (!await SpeakLineAsync(await _negotiation.GenerateWelcomeLine(_state, false))) return false;
                        if (!scale.HasFish) await WaitForPlacementActivityAsync(_placementIdleSeconds);
                        if (!_busy) return false;
                        ReconcileScaleForSummary(scale, rate, maxRate, added, removed);   // pick up replaced fish
                        speakSummary = true;
                        continue;
                    }
                    emptyNudges = 0;

                    if (speakSummary)
                    {
                        float[] prices = _calculator.SplitBundlePrice(_state.fishBundle, _state.currentOffer);
                        MerchantTurnResult summary = await _negotiation.GenerateSaleSummaryLine(
                            _state, prices, _state.currentOffer, FishNames(added), FishNames(removed));
                        PublishOffer();   // the summary read-back voices the current total — lands as it finishes
                        if (!await SpeakLineAsync(summary)) return false;
                        speakSummary = false;
                    }

                    string said = await ListenAsync();
                    if (!_busy) return false;

                    // Silence / failed capture: keep listening — never re-process the PREVIOUS utterance.
                    // (Re-running the LLM on a stale transcript once let a quiet player accidentally re-roll
                    // their last "yes" until it crossed the bar and sealed on silence.)
                    if (string.IsNullOrWhiteSpace(said)) continue;

                    // Catch changed before/while the player spoke → acknowledge it with a fresh summary before
                    // treating their words as haggling ("tell me what changed the next time I speak").
                    if (ReconcileScaleForSummary(scale, rate, maxRate, added, removed)) { speakSummary = true; continue; }

                    // Otherwise the player is confirming, walking away, or HAGGLING the total — run a full turn.
                    _state.turnCount++;
                    if (_maxPlayerTurns > 0 && _state.turnCount > _maxPlayerTurns)
                    {
                        Debug.LogWarning($"{LogPrefix} Hit the turn limit while haggling the summary — ending.");
                        SetTerminal(NegotiationPhase.WalkedAway);
                        return true;
                    }

                    // The summary read-back IS the confirmation prompt, so score the reply against the lenient
                    // "already asked point-blank" bar (AcceptConfirmedScoreThreshold) instead of the strict
                    // unprompted one — a plain yes here should close, not be mistaken for more haggling.
                    _state.awaitingAcceptConfirmation = true;
                    MerchantTurnResult result =
                        await _negotiation.ProcessPlayerTurn(_state, said, _state.isDeceptive);
                    if (!_busy) return false;
                    if (result == null) { speakSummary = true; continue; }   // LLM glitch → just re-read the summary

                    if (IsAcceptanceResult(result))
                    {
                        // Player confirmed — the summary WAS the confirmation prompt, so a clear yes seals (no
                        // second "are you sure?"). Re-verify the scale first, then pay out and close.
                        if (ReconcileScaleForSummary(scale, rate, maxRate, added, removed)) { speakSummary = true; continue; }
                        if (_state.fishBundle.Count == 0) continue;
                        await SealBundleSaleAsync(scale, BuildBundlePriceMap());
                        return true;
                    }
                    if (IsDeclineResult(result))
                    {
                        Debug.Log($"{LogPrefix} Player walked away from the sale at the summary.");
                        SetTerminal(NegotiationPhase.Declined);
                        return true;
                    }

                    // Haggle / other (raise, hold, clarify, or "are you sure you want to stop?"): fold any raised
                    // total into the RATE (offer per unit of fair value), so the whole catch re-prices off it and
                    // the deal stays add/remove-symmetric. Then speak the merchant's own reply.
                    if (result.HasNewOffer && result.NewOfferAmount > 0f)
                    {
                        // Ratchet: the total only ever moves UP toward the ceiling, never down (matches the haggle).
                        _state.currentOffer =
                            Mathf.Round(Mathf.Max(_state.currentOffer, Mathf.Min(result.NewOfferAmount, _state.maxOffer)));
                        _state.offerHistory.Add(_state.currentOffer);
                        float fs = SumFairValue(_state.fishBundle);
                        if (fs > 0f) rate = _state.currentOffer / fs;
                    }
                    if (result.HasNewOffer && result.NewOfferAmount > 0f) PublishOffer();   // raised total, about to be voiced
                    if (!await SpeakLineAsync(result)) return false;
                }

                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} BT sale-confirm phase error: {e}");
                return false;
            }
        }

        // Seals an AGREED bundle: pay out at the frozen per-fish prices, then speak the closing line and end
        // Accepted. Shared by the summary-confirmed path and the summary-disabled path. Pays out BEFORE the
        // close line so a confirmed deal is honoured even if the player walks off while it plays. The caller
        // has already reconciled the bundle against the scale and ensured it is non-empty.
        private async Task SealBundleSaleAsync(ISellScale scale, Dictionary<FishData, float> priceByFish)
        {
            PricesFor(_state.fishBundle, priceByFish, out float agreedTotal);
            PayoutBundle(scale, priceByFish);
            _state.state = NegotiationPhase.Accepted;
            await SpeakLineAsync(await _negotiation.GenerateSaleClosedLine(_state, agreedTotal));
            SetTerminal(NegotiationPhase.Accepted);
        }

        // Snapshots the fish resting on the scale into a priced bundle and rolls the (single) deception
        // for the whole lot, then moves to the Negotiating stage.
        private void SnapshotBundleFromScale(ISellScale scale)
        {
            _state.fishBundle = new List<FishData>(scale.FishDataOnScale);

            bool isDeceptive = _calculator.RollDeception(_activeMerchant.Config);
            _calculator.GenerateBundleEnvelope(_activeMerchant.Config, _state.fishBundle, isDeceptive,
                out float opening, out float max);
            opening = Mathf.Round(opening);
            max = Mathf.Round(max);

            _state.currentOffer = opening;
            _state.offerHistory.Add(opening);
            _state.isDeceptive = isDeceptive;
            _state.maxOffer = max;
            _state.maxRaisePerTurn = _calculator.GenerateMaxRaiseStep(opening, max);
            _state.stage = NegotiationStage.Negotiating;
            _state.state = NegotiationPhase.Negotiating;

            Debug.Log($"{LogPrefix} Catch on scale: {_state.fishBundle.Count} fish — opening {opening:0} EGP " +
                      $"(ceiling {max:0}, deceptive={isDeceptive}).");
        }

        // Removes from the bundle any fish no longer resting on the scale. Returns true if any were dropped.
        private bool ReconcileBundleWithScale(ISellScale scale)
        {
            IReadOnlyList<FishData> onScale = scale.FishDataOnScale;   // stable per-fish references (cached ToFishData)
            int before = _state.fishBundle.Count;
            _state.fishBundle.RemoveAll(f => f == null || !ContainsByRef(onScale, f));
            return _state.fishBundle.Count != before;
        }

        // Folds fish added to / removed from the scale into the running summary deal, then RE-PRICES the whole
        // catch off the fixed negotiation <paramref name="rate"/> (offer per unit of fair value): the total is
        // always rate × the current sum of fair values, never an accumulation of per-fish deltas. That makes
        // add/remove perfectly symmetric — removing a fish then adding the same one back restores the identical
        // total. The ceiling tracks <paramref name="maxRate"/> the same way. Fills added/removed with what
        // changed (for the merchant's spoken acknowledgement) and returns true if anything changed. Fish
        // references are stable per fish (cached ToFishData), so comparison is by reference.
        private bool ReconcileScaleForSummary(ISellScale scale, float rate, float maxRate,
            List<FishData> added, List<FishData> removed)
        {
            added.Clear();
            removed.Clear();
            IReadOnlyList<FishData> onScale = scale.FishDataOnScale;

            foreach (FishData f in _state.fishBundle)
                if (f != null && !ContainsByRef(onScale, f)) removed.Add(f);
            foreach (FishData f in onScale)
                if (f != null && !ContainsByRef(_state.fishBundle, f)) added.Add(f);

            if (added.Count == 0 && removed.Count == 0) return false;

            foreach (FishData f in removed) _state.fishBundle.Remove(f);
            foreach (FishData f in added) _state.fishBundle.Add(f);

            float fairSum = SumFairValue(_state.fishBundle);
            _state.currentOffer = Mathf.Round(rate * fairSum);
            _state.maxOffer = Mathf.Max(_state.currentOffer, Mathf.Round(maxRate * fairSum));
            return true;
        }

        // Sum of the fair market values of the fish in a bundle — the base the negotiation rate multiplies.
        private static float SumFairValue(List<FishData> bundle)
        {
            float sum = 0f;
            foreach (FishData f in bundle)
                if (f != null) sum += f.fairMarketValue;
            return sum;
        }

        // The per-fish price map for the CURRENT bundle at the current total, split proportional to fair value
        // (the same distribution the negotiation rate implies). Built fresh at seal / for the fast path.
        private Dictionary<FishData, float> BuildBundlePriceMap()
        {
            var map = new Dictionary<FishData, float>();
            float[] split = _calculator.SplitBundlePrice(_state.fishBundle, _state.currentOffer);
            for (int i = 0; i < _state.fishBundle.Count; i++)
                if (_state.fishBundle[i] != null) map[_state.fishBundle[i]] = split[i];
            return map;
        }

        // Arabic (fallback transliterated) display names for a set of fish, for the summary's spoken
        // acknowledgement of what the player just added / removed.
        private static IReadOnlyList<string> FishNames(List<FishData> fish)
        {
            if (fish == null || fish.Count == 0) return Array.Empty<string>();
            var names = new List<string>(fish.Count);
            foreach (FishData f in fish)
            {
                if (f == null) continue;
                names.Add(!string.IsNullOrWhiteSpace(f.fishTypeArabic) ? f.fishTypeArabic : f.fishType);
            }
            return names;
        }

        // A processed turn that clearly CLOSES the deal. The game already coerces a clear agreement into one
        // of these actions (the same signal Bt_ApplyResult trusts), so we act on the action, not the raw
        // score. The summary WAS the confirmation prompt, so ConfirmAccept ("are you sure?") also seals here
        // rather than asking a second time.
        private static bool IsAcceptanceResult(MerchantTurnResult r) =>
            r != null && (r.Action == MerchantAction.Accept || r.Action == MerchantAction.ConfirmAccept ||
                          r.Action == MerchantAction.PlayerAccepted);

        // A processed turn where the player is DONE with the sale (not mere price pushback). A first-time
        // "I want to stop" surfaces as ConfirmDecline, which stays in the haggle path so the merchant asks
        // them to confirm before the sale is actually thrown away.
        private static bool IsDeclineResult(MerchantTurnResult r) =>
            r != null && (r.Action == MerchantAction.PlayerDeclined || r.Action == MerchantAction.WalkAway);

        // Per-fish prices (aligned to the bundle) pulled from the frozen map, plus their running total.
        private static float[] PricesFor(List<FishData> bundle, Dictionary<FishData, float> priceByFish, out float total)
        {
            var prices = new float[bundle.Count];
            total = 0f;
            for (int i = 0; i < bundle.Count; i++)
            {
                if (bundle[i] != null && priceByFish.TryGetValue(bundle[i], out float p))
                {
                    prices[i] = p;
                    total += p;
                }
            }
            return prices;   // total is returned via the out parameter
        }

        // Records a sale per fish still on the scale and removes each from the world (fish "disappear").
        // Delegates to the shared BundleSaleSettler so the AI and scripted flows pay out identically.
        private void PayoutBundle(ISellScale scale, Dictionary<FishData, float> priceByFish)
        {
            float total = BundleSaleSettler.Payout(scale, priceByFish, ResolveFishInventory(), _playerStats,
                _calculator, out int soldCount, out float sellScore);
            if (soldCount > 0)
                _eventService?.Publish(new SellScoreEvent(sellScore, total, soldCount));
        }

        private static bool ContainsByRef(IReadOnlyList<FishData> list, FishData fish)
        {
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], fish)) return true;
            return false;
        }

        // Speaks a merchant line and awaits its TTS completion, surfacing the subtitle first. Returns
        // false if the negotiation ended while speaking. Null line (LLM total failure) is treated as done.
        private async Task<bool> SpeakLineAsync(MerchantTurnResult line)
        {
            if (!_busy) return false;
            if (line != null)
            {
                ShowSubtitle(line.SpeechTranslationEnglish, line.MerchantSpeech);
                await SpeakAsync(line.MerchantSpeech);
            }
            else FlushPendingOffer();   // nothing to voice — don't strand an armed offer
            return _busy;
        }

        // Bridges one TTS line to an await (completes when the line finishes OR fails OR is cancelled).
        // Continuations run asynchronously so completing this from Cleanup can't re-enter mid-teardown.
        private Task SpeakAsync(string arabic)
        {
            // Held in a LOCAL as well as the field: PlayMerchantLine completes synchronously when there is
            // no TTS service or no voice id, and the old code read the field back inside the callback and
            // nulled it — so on that path `return _speakTcs.Task` dereferenced a field the callback had
            // already cleared. The field stays only so Cleanup can unblock a line mid-flight.
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _speakTcs = tcs;
            PlayMerchantLine(arabic, () =>
            {
                if (ReferenceEquals(_speakTcs, tcs)) _speakTcs = null;
                tcs.TrySetResult(true);
            });
            return tcs.Task;
        }

        // Bridges one turn-based capture to an await, returning the transcript (or null on failure /
        // cancellation). HandleTranscriptionComplete/Failed complete it; Cleanup unblocks it on teardown.
        // Continuations run asynchronously so completing this from an event handler can't re-enter here.
        private Task<string> ListenAsync()
        {
            _listenTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            BeginListening();
            return _listenTcs.Task;
        }

        /// <summary>Speak step: start TTS of the pending line. <see cref="LineFinished"/> flips when done.</summary>
        public void Bt_PlayPendingLine()
        {
            if (string.IsNullOrEmpty(_pendingLine))
            {
                FlushPendingOffer();    // nothing to voice — don't strand an armed offer
                _lineFinished = true;   // nothing queued to say — treat as immediately finished
                return;
            }
            _lineFinished = false;
            string line = _pendingLine;
            _pendingLine = null;
            PlayMerchantLine(line, () => _lineFinished = true);
        }

        /// <summary>Listen step: arm capture. TranscriptReady/CaptureFailed flip when the utterance resolves.</summary>
        public void Bt_BeginListen()
        {
            _transcriptReady = false;
            _captureFailed = false;
            _lastTranscript = "";
            BeginListening();
        }

        /// <summary>Turn step: run the player's utterance through the LLM. Stores the validated result.</summary>
        public async Task<bool> Bt_ProcessTurnAsync()
        {
            try
            {
                _lastResult = null;
                if (_state == null || _negotiation == null) return false;

                ShowThinkingCue();
                // Tone follows the OPENING roll (fixed offer envelope), not a per-turn re-roll.
                MerchantTurnResult result =
                    await _negotiation.ProcessPlayerTurn(_state, _lastTranscript, _state.isDeceptive);
                HideThinkingCue();

                if (!_busy || result == null) return false;

                _consecutiveRecoveries = 0;   // a clean turn resets the recovery counter
                _lastResult = result;
                return true;
            }
            catch (Exception e)
            {
                HideThinkingCue();
                Debug.LogError($"{LogPrefix} BT process-turn error: {e}");
                return false;
            }
        }

        /// <summary>Apply step: apply the stored turn result; queues the next line, sets terminal on Accept/Walk.</summary>
        public void Bt_ApplyResult()
        {
            if (_lastResult == null) return;   // nothing to apply
            MerchantTurnResult result = _lastResult;
            _lastResult = null;

            _state.turnCount++;
            ApplyTurnLimit(result);
            Debug.Log($"{LogPrefix} Turn {_state.turnCount}: action={result.Action}" +
                      (result.HasNewOffer ? $", newOffer={result.NewOfferAmount:0.#}" : "."));

            // A bundle sale does NOT close at acceptance — the per-fish SUMMARY and the player's confirmation
            // come first, and only then is the deal sealed with a closing line (see Bt_RunSaleConfirmPhaseAsync).
            // So swallow the model's premature "it's a deal" line/subtitle here; the summary speaks next.
            bool bundleAcceptClose = IsBundleSale &&
                (result.Action == MerchantAction.Accept || result.Action == MerchantAction.PlayerAccepted);
            if (!bundleAcceptClose)
            {
                ShowSubtitle(result.SpeechTranslationEnglish, result.MerchantSpeech);
                _pendingLine = result.MerchantSpeech;
            }

            switch (result.Action)
            {
                case MerchantAction.RaiseOffer:
                    // Ratchet: the offer can only ever go UP (see ApplyTurnResult).
                    _state.currentOffer = Mathf.Max(_state.currentOffer, result.NewOfferAmount);
                    _state.offerHistory.Add(_state.currentOffer);
                    _state.state = NegotiationPhase.Negotiating;
                    break;
                case MerchantAction.HoldFirm:
                case MerchantAction.ClarifyRequest:
                    // Holding firm at the ceiling burns hold-at-max patience (see ApplyTurnResult).
                    if (result.Action == MerchantAction.HoldFirm
                        && _state.maxOffer > 0f && _state.currentOffer >= _state.maxOffer)
                        _state.turnsHeldAtMax++;
                    _state.state = NegotiationPhase.Negotiating;
                    break;
                case MerchantAction.ConfirmDecline:
                    // Non-terminal: the merchant asks the player to confirm calling the sale off, and the
                    // graph loops for their answer (see ApplyTurnResult).
                    Debug.Log($"{LogPrefix} Player wants to call the sale off — asking them to confirm.");
                    _state.state = NegotiationPhase.Negotiating;
                    break;
                case MerchantAction.Accept:
                    _state.currentOffer = result.NewOfferAmount;
                    _state.offerHistory.Add(_state.currentOffer);
                    CloseHaggleAccepted();
                    break;
                case MerchantAction.WalkAway:
                    _state.state = NegotiationPhase.WalkedAway;
                    SetTerminal(NegotiationPhase.WalkedAway);
                    break;
                case MerchantAction.PlayerDeclined:
                    // Only reachable after the player confirmed the rejection (see ApplyTurnResult).
                    Debug.Log($"{LogPrefix} Player confirmed the rejection — ending the sale.");
                    _state.state = NegotiationPhase.Declined;
                    SetTerminal(NegotiationPhase.Declined);
                    break;
                case MerchantAction.ConfirmAccept:
                    // Non-terminal: the merchant asks the player to confirm the sale, and the graph loops
                    // for their answer (see ApplyTurnResult). The acceptance mirror of ConfirmDecline.
                    Debug.Log($"{LogPrefix} Player wants to accept — asking them to confirm the sale.");
                    _state.state = NegotiationPhase.Negotiating;
                    break;
                case MerchantAction.PlayerAccepted:
                    // Only reachable after the player confirmed the sale (see ApplyTurnResult). Closes at
                    // the agreed price carried on the result.
                    Debug.Log($"{LogPrefix} Player confirmed the sale — closing the deal.");
                    _state.currentOffer = result.NewOfferAmount;
                    _state.offerHistory.Add(_state.currentOffer);
                    CloseHaggleAccepted();
                    break;
            }

            // Surface the number to the market ticket only when the merchant actually voiced one.
            if (result.HasNewOffer || result.Action == MerchantAction.PlayerAccepted) PublishOffer();
        }

        // A price was agreed in the haggle. Legacy single-fish sale ends here (terminal Accepted). The
        // scale-based bundle sale does NOT end yet: it moves to the AwaitingSaleConfirm stage so the
        // merchant can read the per-fish summary and re-verify the scale before the money changes hands.
        // Leaving the Negotiating stage makes NegotiationOngoing false, so the haggle loop exits into the
        // sale-confirm phase.
        private void CloseHaggleAccepted()
        {
            if (IsBundleSale)
            {
                _state.stage = NegotiationStage.AwaitingSaleConfirm;
                _state.state = NegotiationPhase.Negotiating;
                Debug.Log($"{LogPrefix} Bundle price agreed at {_state.currentOffer:0} EGP — moving to sale summary.");
            }
            else
            {
                _state.state = NegotiationPhase.Accepted;
                SetTerminal(NegotiationPhase.Accepted);
            }
        }

        /// <summary>Recovery step: queue a generic "say that again" line, or give up (terminal) past the limit.</summary>
        public void Bt_RecoverTurn()
        {
            _consecutiveRecoveries++;
            if (_consecutiveRecoveries > MaxConsecutiveRecoveries || IsConnectivityLost())
            {
                Debug.LogWarning($"{LogPrefix} Recovery exhausted/connectivity lost — ending negotiation.");
                SetTerminal(NegotiationPhase.Declined);
                _pendingLine = null;
                return;
            }
            Debug.LogWarning($"{LogPrefix} Recoverable failure — queueing fallback line (attempt {_consecutiveRecoveries}).");
            ShowSubtitle(FallbackEnglish, FallbackArabic);
            _pendingLine = FallbackArabic;
        }

        /// <summary>Resolve step: record the outcome and tear down (notifies the coordinator).</summary>
        public void Bt_Resolve() => EndWithStat(Outcome);

        /// <summary>Fallback step: hand to the scripted fallback + tear down (early-failure branch).</summary>
        public void Bt_ScriptedFallback() =>
            TriggerScriptedFallback(_activeMerchant, _state != null ? _state.fishBeingSold : null);

        /// <summary>
        /// Condition helper for the graph's online/offline SWITCH: true when the ONLINE (AI voice) selling
        /// system should run — scripted mode is not forced AND voice mode (mic + internet consent +
        /// connectivity) is available. When this is false the graph should run the OFFLINE scripted sale
        /// instead (<see cref="Bt_RunScriptedSaleAsync"/>). Backed by the "Use Online Selling" condition node.
        /// </summary>
        public bool Bt_ShouldUseOnlineSelling() =>
            !_forceScriptedMode && _consent != null && _consent.IsVoiceModeAvailable();

        /// <summary>
        /// Runs the OFFLINE (scripted, button-based) sale end-to-end as ONE awaitable graph step — the
        /// offline twin of <see cref="Bt_RunBundleSaleAsync"/>. Starts the scripted sale, awaits its
        /// outcome, and does its own teardown (the sale's onEnded routes to <see cref="Cleanup"/>, which
        /// notifies the coordinator), so NO separate Resolve node is needed after it. Returns true only if
        /// the sale closed Accepted; false on decline / walk-away / when no scripted service or sell scale
        /// is available. Backed by the "Run Offline Sale" action node; use it as the offline branch of the
        /// root Selector.
        /// </summary>
        public Task<bool> Bt_RunScriptedSaleAsync()
        {
            IScriptedSaleService scripted = ResolveScriptedSale();
            if (scripted == null || _activeMerchant == null || _activeMerchant.SellScale == null)
            {
                Debug.LogWarning($"{LogPrefix} Offline sale unavailable for '{NameOf(_activeMerchant)}' " +
                                 "(no scripted service or sell scale) — ending cleanly.");
                if (_busy) Cleanup(NegotiationPhase.Declined);
                return Task.FromResult(false);
            }

            // Drop AI voice capture / event subscriptions before the scripted flow owns the merchant, and
            // take ownership so CancelNegotiation (player walks away) isn't a no-op — see TriggerScriptedFallback.
            Unsubscribe();
            _voiceCapture?.CloseSession();
            _busy = true;

            MerchantNPC merchant = _activeMerchant;
            Debug.Log($"{LogPrefix} Running the offline scripted sale for '{NameOf(merchant)}' (graph branch).");

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool started = scripted.BeginScriptedSale(merchant, phase =>
            {
                SetTerminal(phase);   // record the outcome for the graph/observers
                Cleanup(phase);       // teardown + notify the coordinator (idempotent)
                tcs.TrySetResult(phase == NegotiationPhase.Accepted);
            });

            if (!started)
            {
                if (_busy) Cleanup(NegotiationPhase.Declined);
                return Task.FromResult(false);
            }
            return tcs.Task;
        }

        // --- Helpers / stubs -------------------------------------------------------

        // Decides whether the merchant's opening line should include a greeting, and records that a
        // conversation has started. First conversation with this merchant: always greet (Egyptian
        // style). Afterwards: greet only GreetingChanceAfterFirst of the time.
        /// <summary>What <see cref="SpeakIdiomOpeningAsync"/> did, since an async method cannot use `out`.</summary>
        private enum IdiomOpening
        {
            /// <summary>No idiom this time — the caller should speak the normal LLM welcome.</summary>
            NotSpoken,
            /// <summary>Idiom + placement request both said; the opening is complete.</summary>
            Spoken,
            /// <summary>The negotiation ended mid-line; unwind exactly as for any other line.</summary>
            Aborted
        }

        /// <summary>
        /// Speaks this merchant's whole opening as an idiom, if he is the sort who has one and the roll
        /// goes his way: the proverb, then the "…anyway, put the fish on the scale" line that follows it.
        ///
        /// Both halves are needed. A proverb on its own leaves the player with nothing to act on and no
        /// idea the merchant is waiting, so the follow-up carries the instruction the LLM welcome would
        /// otherwise have carried — which is exactly why the caller then SKIPS that welcome. Nothing
        /// delays the next line afterwards: the placement loop's normal idle wait already holds for
        /// <see cref="_placementIdleSeconds"/> before nudging, so the pair paces itself the same as any
        /// LLM opening would.
        ///
        /// Most approaches say nothing here and return <see cref="IdiomOpening.NotSpoken"/> immediately —
        /// that is the point of it.
        /// </summary>
        private async Task<IdiomOpening> SpeakIdiomOpeningAsync()
        {
            MerchantIdiomsSO idioms = ResolveIdioms();
            if (idioms == null || _activeMerchant == null) return IdiomOpening.NotSpoken;
            if (!idioms.TryPick(_activeMerchant.Config, out MerchantIdiomsSO.Line idiom)) return IdiomOpening.NotSpoken;

            if (!await SpeakIdiomLineAsync(idiom.english, idiom.arabic)) return IdiomOpening.Aborted;

            MerchantIdiomsSO.Line ask = idioms.RandomPlacementRequest();
            if (!await SpeakIdiomLineAsync(ask.english, ask.arabic)) return IdiomOpening.Aborted;

            return IdiomOpening.Spoken;
        }

        // Shows a hand-authored line and voices its Arabic. The subtitle is shown either way; a bank entry
        // with no Arabic yet should still read rather than stall waiting for TTS that will never speak.
        private async Task<bool> SpeakIdiomLineAsync(string english, string arabic)
        {
            if (string.IsNullOrWhiteSpace(english) && string.IsNullOrWhiteSpace(arabic)) return _busy;

            ShowSubtitle(english, arabic);
            if (!string.IsNullOrWhiteSpace(arabic)) await SpeakAsync(arabic);
            return _busy;
        }

        // The bank wired on this controller, else the market-wide one. Both paths read the same asset off
        // MerchantSystemConfig so the AI and scripted sales can never drift onto different proverbs.
        private MerchantIdiomsSO ResolveIdioms() =>
            _idioms != null ? _idioms : (_systemConfig != null ? _systemConfig.Idioms : null);

        private bool DecideOpeningGreeting(MerchantNPC merchant)
        {
            if (merchant == null) return true;

            bool greet = merchant.IsFirstConversation
                         || UnityEngine.Random.value < GreetingChanceAfterFirst;
            merchant.RegisterConversation();
            return greet;
        }

        private FishData ResolveFishToSell()
        {
            // The fish comes from the player's real inventory (or the random test provider standing in for
            // it). With no inventory / no fish there is nothing to sell — the caller declines cleanly.
            IFishInventory inventory = ResolveFishInventory();
            if (inventory != null && inventory.HasFish
                && inventory.TryGetFishToSell(out FishData fish)
                && fish != null && !string.IsNullOrWhiteSpace(fish.fishType))
                return fish;

            Debug.LogWarning($"{LogPrefix} No fish inventory available — nothing to sell.");
            return null;
        }

        // The market catalog from the resolved inventory (species + weight bands), copied so the
        // negotiation state owns it. Null when no inventory / catalog is available (debug-fish path).
        private List<FishCatalogEntry> ResolveFishCatalog()
        {
            IReadOnlyList<FishCatalogEntry> catalog = ResolveFishInventory()?.Catalog;
            return catalog != null && catalog.Count > 0 ? new List<FishCatalogEntry>(catalog) : null;
        }

        private IFishInventory ResolveFishInventory() => _fishInventory.Resolve(_fishInventorySource);

        // Resolves (or creates) the shared voice-capture controller and puts it in turn-based mode.
        private void EnsureVoiceCapture()
        {
            if (_voiceCapture == null)
                _voiceCapture = FindFirstObjectByType<VoiceCaptureController>();
            if (_voiceCapture == null)
                _voiceCapture = gameObject.AddComponent<VoiceCaptureController>();
            _voiceCapture.AutoReArm = false;   // negotiation is turn-based: one utterance per BeginCapture
        }

        private bool IsConnectivityLost()
        {
            if (Application.internetReachability == NetworkReachability.NotReachable) return true;
            if (_consent == null || !_consent.IsVoiceModeAvailable()) return true;
            return false;
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            EnsureVoiceCapture();
            if (_voiceCapture == null) return;
            _voiceCapture.OnTranscription += HandleTranscriptionComplete;
            _voiceCapture.OnPartialTranscription += HandlePartialTranscript;
            _voiceCapture.OnCaptureFailed += HandleTranscriptionFailed;
            _eventService?.Subscribe<FishFumbledEvent>(HandleFishFumbled);
            _eventService?.Subscribe<ScaleContentsChangedEvent>(HandleScaleContentsChanged);
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_voiceCapture != null)
            {
                _voiceCapture.OnTranscription -= HandleTranscriptionComplete;
                _voiceCapture.OnPartialTranscription -= HandlePartialTranscript;
                _voiceCapture.OnCaptureFailed -= HandleTranscriptionFailed;
            }
            _eventService?.Unsubscribe<FishFumbledEvent>(HandleFishFumbled);
            _eventService?.Unsubscribe<ScaleContentsChangedEvent>(HandleScaleContentsChanged);
            _subscribed = false;
            ClearPlayerPartial();   // drop any lingering live caption when the flow detaches
        }

        // Live player partial (mid-utterance) → a player-side caption, ONLY when opted in. Display only:
        // the negotiation decision always uses the final transcript from HandleTranscriptionComplete.
        private void HandlePartialTranscript(string text)
        {
            if (_showPlayerPartials)
                _eventService?.Publish(new PlayerPartialTranscriptEvent(text));
        }

        // Clears the player caption (empty text = hide). No-op when the feature is off.
        private void ClearPlayerPartial()
        {
            if (_showPlayerPartials)
                _eventService?.Publish(new PlayerPartialTranscriptEvent(string.Empty));
        }

        // Flags a fumble to quip about — but only while awaiting fish placement (the only stage a funny
        // "you dropped it" reaction makes sense). Wakes the placement phase IMMEDIATELY so the quip plays
        // the instant the fish is fumbled, instead of waiting for the player to next speak.
        private void HandleFishFumbled(FishFumbledEvent evt)
        {
            if (_state == null || _state.stage != NegotiationStage.AwaitingPlacement) return;
            _pendingFumble = evt.Reason;
            Debug.Log($"{LogPrefix} Fish fumbled ({evt.Reason}) while awaiting placement.");

            // React immediately: wake an idle wait AND interrupt an in-flight "is that all?" listen, so the
            // quip plays the instant the fish is fumbled rather than after the player's next answer.
            WakePlacement();
            TaskCompletionSource<string> listen = _listenTcs;
            _listenTcs = null;
            listen?.TrySetResult(null);
        }

        // Wakes a scale wait when the scale's contents change (a fish placed or removed). During PLACEMENT it
        // notices fish loaded SILENTLY so the merchant can ask whether that's the whole catch; during
        // SALE-CONFIRM it wakes the patient re-placement wait the instant the player puts a bumped-off fish
        // back, instead of stalling for the idle timeout. Only wakes an IDLE wait (WakePlacement is a no-op
        // when nothing is awaiting — e.g. an in-flight confirm-listen keeps waiting for the verbal answer).
        // Our merchant's scale only.
        private void HandleScaleContentsChanged(ScaleContentsChangedEvent evt)
        {
            if (_state == null) return;
            if (_state.stage != NegotiationStage.AwaitingPlacement &&
                _state.stage != NegotiationStage.AwaitingSaleConfirm) return;
            if (_activeMerchant == null || !ReferenceEquals(evt.Scale, _activeMerchant.SellScale)) return;
            WakePlacement();
        }

        // Unblocks an idle placement wait (WaitForPlacementActivityAsync) so it re-evaluates the scale now.
        private void WakePlacement()
        {
            TaskCompletionSource<bool> signal = _placementSignal;
            _placementSignal = null;
            signal?.TrySetResult(true);
        }

        // Waits until the player does something placement-relevant (places/removes fish, or fumbles — both
        // call WakePlacement) or the timeout elapses. Returns true if activity woke it, false on timeout.
        // Does NOT arm the mic — it only watches the scale, so a silent player still times out and is nudged.
        private async Task<bool> WaitForPlacementActivityAsync(float seconds)
        {
            _placementSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task woke = _placementSignal.Task;
            Task finished = await Task.WhenAny(woke, Task.Delay(TimeSpan.FromSeconds(seconds)));
            _placementSignal = null;
            return finished == woke;
        }

        // Announces a merchant line for display. Called the moment the line is settled and never waits
        // on audio, so the subtitle and the speech start together.
        private void ShowSubtitle(string english, string arabic)
        {
            VerboseLog.Trace($"{LogPrefix} [Subtitle] {english}  |  (AR) {arabic}");
            // Also record the merchant's Arabic line to the shared UTF-8 log so the whole conversation
            // (merchant + player transcripts) is reviewable outside the Unity Console.
            _voiceCapture?.AppendLog("MERCHANT", $"{arabic}  |  (EN) {english}");

            if (_eventService == null || _activeMerchant == null) return;
            _eventService.Publish(new MerchantSpokeEvent(_activeMerchant, english, arabic));
        }

        // Announces the price the merchant has just called out, so a scale-side readout (the market
        // ticket) can show only the number he has actually voiced — never the hidden fair value, and
        // never before he says it. Emitted alongside the spoken offer line; no-op until there is one.
        private void PublishOffer()
        {
            if (_eventService == null || _activeMerchant == null || _state == null) return;
            if (_state.currentOffer <= 0f) return;

            // Keep the card in lockstep with the voice: when the line will be spoken aloud, DEFER the publish
            // until that line's TTS finishes (flushed from PlayMerchantLine) so the price is inked in the
            // instant he stops saying it — not while he is still voicing the number. With no live voice
            // (TTS missing/failed, or subtitle-only) there is nothing to wait on, so publish immediately.
            if (IsVoiceLive)
            {
                _offerPending = true;
                _offerPendingAmount = _state.currentOffer;
            }
            else
            {
                PublishOfferNow(_state.currentOffer);
            }
        }

        // Publishes an offer armed by PublishOffer, once its voicing line has finished speaking. Invoked from
        // the TTS-completion path (PlayMerchantLine) so the card updates exactly as the merchant falls silent.
        private void FlushPendingOffer()
        {
            if (!_offerPending) return;
            _offerPending = false;
            PublishOfferNow(_offerPendingAmount);
        }

        private void PublishOfferNow(float amount)
        {
            if (_eventService == null || _activeMerchant == null || amount <= 0f) return;
            _eventService.Publish(new MerchantOfferedPriceEvent(
                _activeMerchant, _activeMerchant.SellScale, amount));
        }

        // Whether this merchant's next line will actually be spoken aloud (so an offer should land as the
        // speech ends) rather than shown subtitle-only. A missing TTS service or a blank voice id means the
        // request is subtitle-only — treated as no live voice, so offers publish at once.
        private bool IsVoiceLive =>
            _tts != null && _activeMerchant != null &&
            !string.IsNullOrWhiteSpace(_activeMerchant.AssignedVoiceId);

        private void ShowThinkingCue()
        {
            // STUB: trigger a 'merchant is thinking' animation/VFX (UI/anim team hooks here).
            Debug.Log($"{LogPrefix} [Cue] merchant thinking...");
        }

        private void HideThinkingCue()
        {
            // STUB: clear the 'merchant is thinking' cue.
            Debug.Log($"{LogPrefix} [Cue] merchant thinking done.");
        }

        private static string NameOf(MerchantNPC merchant)
        {
            if (merchant == null) return "<none>";
            return merchant.Config != null ? merchant.Config.MerchantName : merchant.name;
        }
    }
}
