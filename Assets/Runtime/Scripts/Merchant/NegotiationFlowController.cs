using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        // Cap on SILENT re-listens while waiting for the player (blank captures / transient STT
        // glitches) before we quietly hand off. This only guards against noise/echo/glitch loops —
        // genuine silence produces no STT events at all, so the merchant simply stays quiet.
        private const int MaxSilentRetries = 8;

        // Greeting policy: the FIRST conversation with a merchant always opens with an Egyptian-style
        // greeting; on later conversations a greeting is used only this fraction of the time.
        private const float GreetingChanceAfterFirst = 0.4f;

        public static NegotiationFlowController Singleton { get; private set; }

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

        [Header("Stubs (temporary)")]
        [Tooltip("STUB fish used when no IFishInventory is available. " +
                 "Set fishType/fairMarketValue/weight to exercise the flow in-editor.")]
        [SerializeField] private FishData _debugFish = new FishData();

        private PriceNegotiationCalculator _calculator;
        private NegotiationState _state;
        private MerchantNPC _activeMerchant;
        private AudioSource _activeAudioSource;
        private IFishInventory _fishInventory;
        private bool _busy;
        private bool _subscribed;
        private int _consecutiveRecoveries;
        private int _silentRetries;   // blank captures / transient STT glitches while WAITING for the player

        // --- Behavior-graph step state (used only on the BT path; see the Bt_* methods) ---
        private string _pendingLine;                  // merchant line waiting to be spoken by a node
        private bool _lineFinished;                   // set true when the current TTS line finishes/fails
        private bool _transcriptReady;                // set true when a player transcript has arrived
        private bool _captureFailed;                  // set true when STT capture failed
        private string _lastTranscript = "";          // the most recent player transcript
        private MerchantTurnResult _lastResult;       // the most recent validated LLM turn result

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

            // BEHAVIOR-GRAPH PATH: hand control to the Behavior graph, whose nodes call the Bt_* steps
            // below. Fish resolution, consent gating, opening, the turn loop and recovery all live in
            // the graph now. (Falls through to the legacy coded flow when no graph is assigned.)
            if (UseBehaviorGraph)
            {
                _busy = true;
                Subscribe();
                _agent.Restart();   // run the graph from the root; nodes call the Bt_* steps below
                Debug.Log($"{LogPrefix} Begin BT negotiation with '{NameOf(merchant)}'.");
                return;
            }

            FishData fish = ResolveFishToSell();
            if (fish == null)
            {
                Debug.LogWarning($"{LogPrefix} No fish to sell; aborting with '{NameOf(merchant)}'.");
                Cleanup(NegotiationPhase.Declined);
                return;
            }

            // POINT 1: consent + connectivity gate for the whole AI path.
            if (ConsentManager.Singleton == null)
            {
                Debug.Log($"{LogPrefix} No ConsentManager — scripted fallback for '{NameOf(merchant)}'.");
                TriggerScriptedFallback(merchant, fish);
                return;
            }

            if (ConsentManager.Singleton.IsVoiceModeAvailable())
            {
                StartAiNegotiation(merchant, fish);
                return;
            }

            // Voice unavailable. If the player has simply never been ASKED, this first approach is
            // the natural moment to ask: show the consent prompt (ConsentPromptUI) and continue on
            // whichever path their answer allows. Only a real decline / missing connectivity falls
            // back to scripted dialogue.
            ConsentState consent = ConsentManager.Singleton.GetConsentStatus();
            bool neverAsked = consent.MicConsent == ConsentStatus.NotAsked
                              || consent.InternetConsent == ConsentStatus.NotAsked;
            if (neverAsked)
            {
                Debug.Log($"{LogPrefix} Consent not asked yet — prompting before negotiating with '{NameOf(merchant)}'.");
                ConsentManager.Singleton.RequestConsent((mic, internet) =>
                {
                    if (_activeMerchant != merchant) return; // torn down while the prompt was up

                    if (ConsentManager.Singleton != null && ConsentManager.Singleton.IsVoiceModeAvailable())
                    {
                        StartAiNegotiation(merchant, fish);
                    }
                    else
                    {
                        Debug.Log($"{LogPrefix} Consent declined/unavailable — scripted fallback for '{NameOf(merchant)}'.");
                        TriggerScriptedFallback(merchant, fish);
                    }
                });
                return;
            }

            Debug.Log($"{LogPrefix} Voice mode unavailable — scripted fallback for '{NameOf(merchant)}'.");
            TriggerScriptedFallback(merchant, fish);
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

                if (MerchantNegotiationLLM.Singleton == null)
                {
                    Debug.LogWarning($"{LogPrefix} LLM unavailable; cannot phrase opening — scripted fallback.");
                    TriggerScriptedFallback(merchant, fish);
                    return;
                }

                MerchantTurnResult opener =
                    await MerchantNegotiationLLM.Singleton.GenerateOpeningLine(_state, isDeceptive, DecideOpeningGreeting(merchant));

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

                if (MerchantNegotiationLLM.Singleton == null)
                {
                    Debug.LogWarning($"{LogPrefix} LLM client missing during turn — scripted fallback.");
                    HideThinkingCue();
                    ConnectivityLossRecovery();
                    return;
                }

                // Tone follows the OPENING roll for the whole negotiation, matching the fixed
                // offer envelope — not re-rolled per turn.
                MerchantTurnResult result =
                    await MerchantNegotiationLLM.Singleton.ProcessPlayerTurn(_state, text, _state.isDeceptive);

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
                    // The player refused the sale: farewell line, then resolve as Declined.
                    _state.state = NegotiationPhase.Declined;
                    PlayMerchantLine(result.MerchantSpeech, () => EndWithStat(NegotiationPhase.Declined));
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

                if (PlayerStats.Singleton != null)
                    PlayerStats.Singleton.RecordSale(record);
                else
                    Debug.LogWarning($"{LogPrefix} PlayerStats missing; sale of {record.fishType} not recorded.");

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

        // Idempotent teardown (keyed on _activeMerchant). Notifies the spawner so it releases its
        // lock and starts the cooldown for the next approach (point 7).
        private void Cleanup(NegotiationPhase phase)
        {
            MerchantNPC merchant = _activeMerchant;
            if (merchant == null) return; // already cleaned up

            CancelInvoke(nameof(ReListen));   // drop any pending silent re-listen
            Unsubscribe();
            _voiceCapture?.CloseSession();
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

        // --- Behavior-graph steps (called by the custom nodes in Merchant/Behavior) ----------------
        //
        // Each Bt_* method is one node's payload. Async steps return a Task the node polls; TTS/STT
        // steps flip a flag (LineFinished / TranscriptReady / CaptureFailed) the node polls. The graph
        // owns the sequencing, the loop and the branches — these just do one step of work each.

        /// <summary>True while the negotiation should keep looping (busy and not yet terminal).</summary>
        public bool NegotiationOngoing => _busy && !IsTerminal;

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
        }

        private void SetTerminal(NegotiationPhase phase)
        {
            IsTerminal = true;
            Outcome = phase;
        }

        /// <summary>Condition: is voice mode (mic + internet consent + connectivity) available?</summary>
        public bool Bt_ConsentAvailable() =>
            ConsentManager.Singleton != null && ConsentManager.Singleton.IsVoiceModeAvailable();

        /// <summary>Setup step: resolve the fish, build the negotiation state, warm the STT session.</summary>
        public bool Bt_Setup()
        {
            if (_activeMerchant == null || _activeMerchant.Config == null) return false;

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

        /// <summary>Opening step: deterministic offer + deception roll, then LLM phrasing. Stores the line.</summary>
        public async Task<bool> Bt_RunOpeningAsync()
        {
            try
            {
                if (_state == null || MerchantNegotiationLLM.Singleton == null) return false;

                bool isDeceptive = _calculator.RollDeception(_activeMerchant.Config);
                float opening = Mathf.Round(_calculator.GenerateOpeningOffer(_activeMerchant.Config, _state.fishBeingSold, isDeceptive));
                _state.currentOffer = opening;
                _state.offerHistory.Add(opening);
                _state.isDeceptive = isDeceptive;
                _state.maxOffer = Mathf.Round(_calculator.GenerateMaxOffer(_activeMerchant.Config, _state.fishBeingSold, isDeceptive, opening));
                _state.maxRaisePerTurn = _calculator.GenerateMaxRaiseStep(opening, _state.maxOffer);
                Debug.Log($"{LogPrefix} Opening offer {opening:0} EGP (ceiling {_state.maxOffer:0}, " +
                          $"raise step ≤{_state.maxRaisePerTurn:0}) for {_state.fishBeingSold.fishType} (deceptive={isDeceptive}).");

                MerchantTurnResult opener = await MerchantNegotiationLLM.Singleton.GenerateOpeningLine(
                    _state, isDeceptive, DecideOpeningGreeting(_activeMerchant));
                if (!_busy || opener == null) return false;

                _state.state = NegotiationPhase.Negotiating;
                ShowSubtitle(opener.SpeechTranslationEnglish, opener.MerchantSpeech);
                _pendingLine = opener.MerchantSpeech;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} BT opening step error: {e}");
                return false;
            }
        }

        /// <summary>Speak step: start TTS of the pending line. <see cref="LineFinished"/> flips when done.</summary>
        public void Bt_PlayPendingLine()
        {
            if (string.IsNullOrEmpty(_pendingLine))
            {
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
                if (_state == null || MerchantNegotiationLLM.Singleton == null) return false;

                ShowThinkingCue();
                // Tone follows the OPENING roll (fixed offer envelope), not a per-turn re-roll.
                MerchantTurnResult result =
                    await MerchantNegotiationLLM.Singleton.ProcessPlayerTurn(_state, _lastTranscript, _state.isDeceptive);
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
            Debug.Log($"{LogPrefix} Turn {_state.turnCount}: action={result.Action}" +
                      (result.HasNewOffer ? $", newOffer={result.NewOfferAmount:0.#}" : "."));
            ShowSubtitle(result.SpeechTranslationEnglish, result.MerchantSpeech);
            _pendingLine = result.MerchantSpeech;

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
                case MerchantAction.Accept:
                    _state.currentOffer = result.NewOfferAmount;
                    _state.offerHistory.Add(_state.currentOffer);
                    _state.state = NegotiationPhase.Accepted;
                    SetTerminal(NegotiationPhase.Accepted);
                    break;
                case MerchantAction.WalkAway:
                    _state.state = NegotiationPhase.WalkedAway;
                    SetTerminal(NegotiationPhase.WalkedAway);
                    break;
                case MerchantAction.PlayerDeclined:
                    _state.state = NegotiationPhase.Declined;
                    SetTerminal(NegotiationPhase.Declined);
                    break;
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

        /// <summary>Resolve step: record the outcome and tear down (notifies the spawner).</summary>
        public void Bt_Resolve() => EndWithStat(Outcome);

        /// <summary>Fallback step: hand to the scripted fallback + tear down (early-failure branch).</summary>
        public void Bt_ScriptedFallback() =>
            TriggerScriptedFallback(_activeMerchant, _state != null ? _state.fishBeingSold : null);

        // --- Helpers / stubs -------------------------------------------------------

        // Decides whether the merchant's opening line should include a greeting, and records that a
        // conversation has started. First conversation with this merchant: always greet (Egyptian
        // style). Afterwards: greet only GreetingChanceAfterFirst of the time.
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
            // Prefer an IFishInventory (real inventory later, random test provider now); fall back to
            // the Inspector debug fish so the flow is still exercisable without one.
            IFishInventory inventory = ResolveFishInventory();
            if (inventory != null && inventory.HasFish
                && inventory.TryGetFishToSell(out FishData fish)
                && fish != null && !string.IsNullOrWhiteSpace(fish.fishType))
                return fish;

            if (_debugFish == null || string.IsNullOrWhiteSpace(_debugFish.fishType))
            {
                Debug.LogWarning($"{LogPrefix} No fish inventory available and debug fish is unset.");
                return null;
            }
            return _debugFish;
        }

        // The market catalog from the resolved inventory (species + weight bands), copied so the
        // negotiation state owns it. Null when no inventory / catalog is available (debug-fish path).
        private List<FishCatalogEntry> ResolveFishCatalog()
        {
            IReadOnlyList<FishCatalogEntry> catalog = ResolveFishInventory()?.Catalog;
            return catalog != null && catalog.Count > 0 ? new List<FishCatalogEntry>(catalog) : null;
        }

        // Resolves an IFishInventory: the serialized reference, else any component in the scene that
        // implements it. Cached after the first successful resolve.
        private IFishInventory ResolveFishInventory()
        {
            if (_fishInventory != null) return _fishInventory;

            if (_fishInventorySource is IFishInventory fromField)
            {
                _fishInventory = fromField;
                return _fishInventory;
            }

            foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb is IFishInventory inv)
                {
                    _fishInventory = inv;
                    return _fishInventory;
                }
            }
            return null;
        }

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
            if (ConsentManager.Singleton == null || !ConsentManager.Singleton.IsVoiceModeAvailable()) return true;
            return false;
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            EnsureVoiceCapture();
            if (_voiceCapture == null) return;
            _voiceCapture.OnTranscription += HandleTranscriptionComplete;
            _voiceCapture.OnCaptureFailed += HandleTranscriptionFailed;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_voiceCapture != null)
            {
                _voiceCapture.OnTranscription -= HandleTranscriptionComplete;
                _voiceCapture.OnCaptureFailed -= HandleTranscriptionFailed;
            }
            _subscribed = false;
        }

        private void ShowSubtitle(string english, string arabic)
        {
            // STUB: UI team hooks subtitle display here. Called immediately, independent of audio.
            Debug.Log($"{LogPrefix} [Subtitle] {english}  |  (AR) {arabic}");
            // Also record the merchant's Arabic line to the shared UTF-8 log so the whole conversation
            // (merchant + player transcripts) is reviewable outside the Unity Console.
            _voiceCapture?.AppendLog("MERCHANT", $"{arabic}  |  (EN) {english}");
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
