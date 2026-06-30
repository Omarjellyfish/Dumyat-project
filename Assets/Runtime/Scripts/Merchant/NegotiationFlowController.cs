using System;
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
    /// Lifecycle handoff: the <see cref="MerchantSpawner"/> calls <see cref="BeginNegotiation"/>;
    /// when the negotiation ends this calls <c>merchant.EndNegotiation(phase)</c>, which the spawner
    /// listens to in order to release its one-at-a-time lock and start the cooldown.
    /// </summary>
    public class NegotiationFlowController : MonoBehaviour, IManager
    {
        private const string LogPrefix = "[Negotiation]";

        // Generic recoverable-failure line (STT/LLM glitch). Egyptian Arabic + English subtitle.
        private const string FallbackArabic = "آسف، ممكن تقول تاني؟";
        private const string FallbackEnglish = "Sorry, can you say that again?";

        // Stop looping the recover-and-relisten path forever if every attempt keeps failing.
        private const int MaxConsecutiveRecoveries = 3;

        public static NegotiationFlowController Singleton { get; private set; }

        [Header("Config")]
        [Tooltip("Global merchant system config (guard-rails, cooldown). Feeds the price calculator.")]
        [SerializeField] private MerchantSystemConfig _systemConfig;

        [Tooltip("AudioSource used for merchant speech when the merchant GameObject has none of its own.")]
        [SerializeField] private AudioSource _fallbackAudioSource;

        [Header("Stubs (temporary)")]
        [Tooltip("STUB fish used until the inventory system provides the caught fish to sell. " +
                 "Set fishType/fairMarketValue/weight to exercise the flow in-editor.")]
        [SerializeField] private FishData _debugFish = new FishData();

        private PriceNegotiationCalculator _calculator;
        private NegotiationState _state;
        private MerchantNPC _activeMerchant;
        private AudioSource _activeAudioSource;
        private bool _busy;
        private bool _subscribed;
        private int _consecutiveRecoveries;

        /// <summary>True while a negotiation (AI or scripted-fallback handoff) is in progress.</summary>
        public bool IsNegotiating => _busy;

        private void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }
            Singleton = this;
            DontDestroyOnLoad(gameObject);

            _calculator = new PriceNegotiationCalculator(_systemConfig);
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        // --- Entry point (called by MerchantSpawner) -------------------------------

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

            if (merchant.Config == null)
            {
                Debug.LogWarning($"{LogPrefix} Merchant '{merchant.name}' has no MerchantConfig; aborting.");
                Cleanup(NegotiationPhase.Declined);
                return;
            }

            FishData fish = ResolveFishToSell();
            if (fish == null)
            {
                Debug.LogWarning($"{LogPrefix} No fish to sell; aborting with '{NameOf(merchant)}'.");
                Cleanup(NegotiationPhase.Declined);
                return;
            }

            // Prefer the merchant's own AudioSource (spatial VR audio); fall back to the shared one.
            _activeAudioSource = merchant.GetComponent<AudioSource>();
            if (_activeAudioSource == null) _activeAudioSource = _fallbackAudioSource;

            // POINT 1: consent + connectivity gate for the whole AI path.
            if (ConsentManager.Singleton == null || !ConsentManager.Singleton.IsVoiceModeAvailable())
            {
                Debug.Log($"{LogPrefix} Voice mode unavailable — scripted fallback for '{NameOf(merchant)}'.");
                TriggerScriptedFallback(merchant, fish);
                return;
            }

            _state = new NegotiationState
            {
                currentMerchant = merchant.Config,
                fishBeingSold = fish,
                state = NegotiationPhase.Approaching,
                turnCount = 0
            };
            _busy = true;
            Subscribe();

            Debug.Log($"{LogPrefix} Begin AI negotiation with '{NameOf(merchant)}' over {fish.fishType}.");
            RunOpeningFlow(merchant, fish);
        }

        // --- Opening (point 2 & 3) -------------------------------------------------

        private async void RunOpeningFlow(MerchantNPC merchant, FishData fish)
        {
            try
            {
                // POINT 2: deterministic opening offer + deception roll; the LLM only PHRASES it.
                bool isDeceptive = _calculator.RollDeception(merchant.Config);
                float opening = _calculator.GenerateOpeningOffer(merchant.Config, fish, isDeceptive);
                _state.currentOffer = opening;
                _state.offerHistory.Add(opening);
                Debug.Log($"{LogPrefix} Opening offer {opening:0.#} for {fish.fishType} (deceptive={isDeceptive}).");

                if (MerchantNegotiationLLM.Singleton == null)
                {
                    Debug.LogWarning($"{LogPrefix} LLM unavailable; cannot phrase opening — scripted fallback.");
                    TriggerScriptedFallback(merchant, fish);
                    return;
                }

                MerchantTurnResult opener =
                    await MerchantNegotiationLLM.Singleton.GenerateOpeningLine(_state, isDeceptive);

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

            if (STTManager.Singleton == null || !STTManager.Singleton.HasAvailableModel)
            {
                Debug.LogWarning($"{LogPrefix} No STT model available; cannot listen — scripted fallback.");
                ConnectivityLossRecovery();
                return;
            }

            Debug.Log($"{LogPrefix} Listening for player response...");
            STTManager.Singleton.StartListening();
        }

        // --- Player turn (point 5) -------------------------------------------------

        private async void HandleTranscriptionComplete(string text)
        {
            if (!_busy) return;

            try
            {
                Debug.Log($"{LogPrefix} Player said: \"{text}\"");
                ShowThinkingCue();

                if (MerchantNegotiationLLM.Singleton == null)
                {
                    Debug.LogWarning($"{LogPrefix} LLM client missing during turn — scripted fallback.");
                    HideThinkingCue();
                    ConnectivityLossRecovery();
                    return;
                }

                bool isDeceptive = _calculator.RollDeception(_state.currentMerchant);
                MerchantTurnResult result =
                    await MerchantNegotiationLLM.Singleton.ProcessPlayerTurn(_state, text, isDeceptive);

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

            if (IsConnectivityLost())
                ConnectivityLossRecovery();   // point 6
            else
                RecoverWithFallbackLine();    // point 5
        }

        // Applies the LLM's action to game state IMMEDIATELY, then plays the merchant's line in
        // parallel (point 5). Non-terminal actions re-open the mic after the line; terminal actions
        // resolve after the line so the closing/farewell is heard before the spawner moves on.
        private void ApplyTurnResult(MerchantTurnResult result)
        {
            _state.turnCount++;
            Debug.Log($"{LogPrefix} Turn {_state.turnCount}: action={result.Action}" +
                      (result.HasNewOffer ? $", newOffer={result.NewOfferAmount:0.#}" : "."));

            // Subtitle shows immediately — never wait on audio (points 3 & 5).
            ShowSubtitle(result.SpeechTranslationEnglish, result.MerchantSpeech);

            switch (result.Action)
            {
                case MerchantAction.RaiseOffer:
                    _state.currentOffer = result.NewOfferAmount;
                    _state.offerHistory.Add(_state.currentOffer);
                    _state.state = NegotiationPhase.Negotiating;
                    PlayMerchantLine(result.MerchantSpeech, BeginListening);
                    break;

                case MerchantAction.HoldFirm:
                case MerchantAction.ClarifyRequest:
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
            }
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

        // STUB for the later scripted-dialogue task. For now it logs a clear TODO and ends cleanly so
        // the spawner can proceed; the real system should take over the negotiation here instead.
        private void TriggerScriptedFallback(MerchantNPC merchant, FishData fish)
        {
            string fishName = fish != null ? fish.fishType : "<no fish>";
            Debug.LogWarning($"{LogPrefix} TODO scripted fallback: '{NameOf(merchant)}' / {fishName} " +
                             "(scripted dialogue system not implemented yet). Ending cleanly so the player isn't stuck.");
            Cleanup(NegotiationPhase.Declined);
        }

        // --- Resolution (point 7) --------------------------------------------------

        private void EndWithStat(NegotiationPhase phase)
        {
            if (_activeMerchant == null) return; // already resolved

            float fair = _state != null && _state.fishBeingSold != null ? _state.fishBeingSold.fairMarketValue : 0f;

            // Success rate only means something for an accepted sale; a walk-away scores 0 (no sale).
            float successRate = phase == NegotiationPhase.Accepted
                ? _calculator.CalculateSuccessRate(_state.currentOffer, fair)
                : 0f;

            if (PlayerStats.Singleton != null)
                PlayerStats.Singleton.RecordSale(successRate);
            else
                Debug.LogWarning($"{LogPrefix} PlayerStats missing; success {successRate:0.#} not recorded.");

            Debug.Log($"{LogPrefix} Resolved {phase} — final price {(_state != null ? _state.currentOffer : 0f):0.#}, " +
                      $"success {successRate:0.#}.");

            Cleanup(phase);
        }

        // Idempotent teardown (keyed on _activeMerchant). Notifies the spawner so it releases its
        // lock and starts the cooldown for the next approach (point 7).
        private void Cleanup(NegotiationPhase phase)
        {
            MerchantNPC merchant = _activeMerchant;
            if (merchant == null) return; // already cleaned up

            Unsubscribe();
            _activeMerchant = null;
            _state = null;
            _activeAudioSource = null;
            _busy = false;

            Debug.Log($"{LogPrefix} Cleanup ({phase}); notifying spawner.");
            merchant.EndNegotiation(phase); // -> MerchantSpawner.HandleNegotiationEnded (lock + cooldown)
        }

        // --- External call wrappers ------------------------------------------------

        // Plays a merchant line via TTS. onDone fires when audio finishes OR fails, so the flow
        // never stalls on audio — the subtitle is already on screen (point 6).
        private void PlayMerchantLine(string arabicText, Action onDone)
        {
            if (TTSManager.Singleton == null)
            {
                Debug.LogWarning($"{LogPrefix} TTS manager unavailable; continuing on subtitle only.");
                onDone?.Invoke();
                return;
            }

            // Voice is the one assigned to THIS NPC at spawn (stable across the whole negotiation),
            // not a per-archetype/per-call value.
            string voiceId = _activeMerchant != null ? _activeMerchant.AssignedVoiceId : null;
            TTSManager.Singleton.PlaySpeech(
                voiceId, arabicText, _activeAudioSource,
                onComplete: onDone,
                onError: () =>
                {
                    Debug.LogWarning($"{LogPrefix} TTS failed; continuing on subtitle only.");
                    onDone?.Invoke();
                });
        }

        // --- Helpers / stubs -------------------------------------------------------

        private FishData ResolveFishToSell()
        {
            // STUB: until the inventory provides the caught fish, use a debug fish from the Inspector.
            // TODO: query the player's inventory (e.g. InventorySystem.GetFishItems()) and map to FishData.
            if (_debugFish == null || string.IsNullOrWhiteSpace(_debugFish.fishType))
            {
                Debug.LogWarning($"{LogPrefix} Debug fish is unset and inventory is not wired yet.");
                return null;
            }
            return _debugFish;
        }

        private bool IsConnectivityLost()
        {
            if (Application.internetReachability == NetworkReachability.NotReachable) return true;
            if (ConsentManager.Singleton == null || !ConsentManager.Singleton.IsVoiceModeAvailable()) return true;
            return false;
        }

        private void Subscribe()
        {
            if (_subscribed || STTManager.Singleton == null) return;
            STTManager.Singleton.OnTranscriptionComplete += HandleTranscriptionComplete;
            STTManager.Singleton.OnTranscriptionFailed += HandleTranscriptionFailed;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (STTManager.Singleton != null)
            {
                STTManager.Singleton.OnTranscriptionComplete -= HandleTranscriptionComplete;
                STTManager.Singleton.OnTranscriptionFailed -= HandleTranscriptionFailed;
            }
            _subscribed = false;
        }

        private void ShowSubtitle(string english, string arabic)
        {
            // STUB: UI team hooks subtitle display here. Called immediately, independent of audio.
            Debug.Log($"{LogPrefix} [Subtitle] {english}  |  (AR) {arabic}");
        }

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
