using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The dialogue action the LLM declares for a turn. Overlaps with the deterministic
    /// <see cref="NegotiationOutcome"/> (Accept/RaiseOffer/HoldFirm/WalkAway) but adds
    /// <see cref="ClarifyRequest"/> for when the player's speech was unclear and
    /// <see cref="PlayerDeclined"/> for when the player clearly refuses to sell / ends the
    /// conversation. NOTE: deception is NOT in here — whether the merchant lowballs is decided
    /// deterministically by <see cref="PriceNegotiationCalculator"/> and passed in; the LLM only
    /// colours the tone.
    /// </summary>
    public enum MerchantAction
    {
        RaiseOffer,
        HoldFirm,
        Accept,
        WalkAway,
        ClarifyRequest,

        /// <summary>
        /// The PLAYER clearly refused to sell or ended the conversation, AND has already confirmed it
        /// when asked. The merchant speaks a short good-natured farewell and the negotiation resolves
        /// as <see cref="NegotiationPhase.Declined"/>. A decline is NEVER applied without a preceding
        /// <see cref="ConfirmDecline"/> turn — the validator coerces an unconfirmed one.
        /// </summary>
        PlayerDeclined,

        /// <summary>
        /// The player just stated they no longer want to sell. Before the sale is thrown away the
        /// merchant asks them to CONFIRM ("are you sure you don't want to sell at all?") and keeps the
        /// negotiation open for exactly one more turn. The next turn resolves it: an unmistakable yes
        /// becomes <see cref="PlayerDeclined"/>; anything else resumes haggling.
        /// </summary>
        ConfirmDecline,

        /// <summary>
        /// The PLAYER clearly agreed to sell at the merchant's standing offer, AND has already confirmed
        /// it when asked. The merchant speaks a short warm closing line and the negotiation resolves as
        /// <see cref="NegotiationPhase.Accepted"/> (the sale is recorded). The exact acceptance mirror of
        /// <see cref="PlayerDeclined"/>: never applied without a preceding <see cref="ConfirmAccept"/>
        /// turn, and driven by the game's threshold on <see cref="MerchantTurnResult.AcceptanceScore"/>,
        /// not by the model choosing to close.
        /// </summary>
        PlayerAccepted,

        /// <summary>
        /// The player just agreed to sell at the standing offer. Before the sale is locked in the merchant
        /// asks them to CONFIRM ("so we're agreed, you'll sell it to me?") and keeps the negotiation open
        /// for exactly one more turn. The next turn resolves it: an unmistakable yes becomes
        /// <see cref="PlayerAccepted"/>; anything else resumes haggling. The acceptance mirror of
        /// <see cref="ConfirmDecline"/>.
        /// </summary>
        ConfirmAccept
    }

    /// <summary>
    /// Validated result of one negotiation turn: what the merchant says (Arabic + English
    /// subtitle), the action taken, and — only for <see cref="MerchantAction.RaiseOffer"/>
    /// / <see cref="MerchantAction.Accept"/> — a sanity-checked new offer.
    /// </summary>
    public class MerchantTurnResult
    {
        public string MerchantSpeech;
        public string SpeechTranslationEnglish;
        public MerchantAction Action;
        public float NewOfferAmount;

        /// <summary>
        /// The price the fisherman (seller) is currently asking, as reported by the model this turn
        /// (0 = none/unclear). Recorded into the negotiation's ask history so the merchant can notice
        /// a raised ask on later turns.
        /// </summary>
        public float PlayerAskedPrice;

        /// <summary>
        /// The fish species the model understood the fisherman to have EXPLICITLY named (empty if they
        /// have not named one yet). Used to hard-gate pricing (the merchant may not price a fish it was
        /// never told) and to persist the deal's fish data. NEVER inferred from weight/catalog.
        /// </summary>
        public string UnderstoodFishType;

        /// <summary>
        /// The weight (kg) the model understood the fisherman to have EXPLICITLY stated (0 if not stated
        /// yet). Paired with <see cref="UnderstoodFishType"/> for the pricing gate and the saved record.
        /// </summary>
        public float UnderstoodWeightKg;

        /// <summary>
        /// True when this turn is the merchant's spoken WARNING that it is about to walk away. Recorded
        /// on <see cref="NegotiationState.walkAwayWarningGiven"/> so a later
        /// <see cref="MerchantAction.WalkAway"/> is allowed to actually end the negotiation.
        /// </summary>
        public bool IsFinalWarning;

        /// <summary>
        /// 0–100: how clearly the model judges the player to be ENDING the sale — before the merchant has
        /// asked, how clearly they want out at all; once asked, how clearly they answered YES. A
        /// MEASUREMENT the model reports, never a decision it acts on: the game thresholds it (see
        /// <see cref="MerchantNegotiationLLM.WithdrawalScoreThreshold"/> /
        /// <see cref="MerchantNegotiationLLM.DeclineConfirmedScoreThreshold"/>) and coerces the action.
        /// </summary>
        public float RejectionScore;

        /// <summary>
        /// 0–100: how clearly the model judges the player to be AGREEING to sell at the merchant's standing
        /// offer — before the merchant has asked, how clearly they are saying yes at all; once asked, how
        /// clearly they answered YES. The acceptance mirror of <see cref="RejectionScore"/>: a MEASUREMENT
        /// the model reports, never a decision it acts on. The game thresholds it (see
        /// <see cref="MerchantNegotiationLLM.AcceptanceScoreThreshold"/> /
        /// <see cref="MerchantNegotiationLLM.AcceptConfirmedScoreThreshold"/>) and coerces the action.
        /// </summary>
        public float AcceptanceScore;

        /// <summary>True when <see cref="NewOfferAmount"/> is meaningful for this action.</summary>
        public bool HasNewOffer => Action == MerchantAction.RaiseOffer || Action == MerchantAction.Accept;
    }

    /// <summary>
    /// Wraps Google Gemini to generate the merchant's negotiation dialogue and per-turn
    /// action as a STRICT structured JSON object.
    ///
    /// "On rails" by design: the model never decides game-affecting facts on its own. Deception
    /// is rolled deterministically by <see cref="PriceNegotiationCalculator"/> and handed in;
    /// any offer the model proposes is clamped/rejected against the merchant's real economics
    /// before the game trusts it; and the model is forced to emit ONLY the JSON schema so it
    /// cannot go off-topic, break character, or promise something the game logic can't honour.
    /// If the model violates the contract we treat it as a recoverable failure and fall back —
    /// we never let unvalidated model output drive the sale.
    ///
    /// Transport is delegated to <see cref="LLMManager"/> (reused per project convention): it pools
    /// <see cref="ILLMModel"/> providers (e.g. Gemini), owns model selection + cross-provider
    /// fallback, and each model owns its own API key/REST/timeout/auth. This class owns prompt
    /// construction, the JSON contract, validation, and a SECOND retry layer specific to
    /// malformed/off-rails structured output.
    /// </summary>
    public class MerchantNegotiationLLM : MonoBehaviour, IMerchantNegotiationService
    {
        // Injected pooled AI services + event bus (replace the former LLMManager/TTSManager singletons).
        private ILLMService _llm;
        private ITTSService _tts;
        private IEventService _events;

        [Inject]
        private void Inject(ILLMService llm, ITTSService tts, IEventService events)
        {
            _llm = llm;
            _tts = tts;
            _events = events;
        }

        // An offer this far above fair value is treated as the model hallucinating, not haggling.
        private const float HardOfferCapMultiplier = 2f;

        // --- Withdrawal thresholds (see MerchantTurnResult.RejectionScore) ---------
        //
        // The model MEASURES how clearly the player is ending the sale; these constants are where the
        // game DECIDES. Two different bars, because the two questions carry different risk:

        /// <summary>
        /// Score at or above which an unprompted statement counts as the player wanting out, and the
        /// merchant must stop and ask them to confirm. Set high: mistaking ordinary price-refusal for a
        /// withdrawal interrupts a healthy haggle.
        /// </summary>
        public const float WithdrawalScoreThreshold = 70f;

        /// <summary>
        /// Score at or above which the player's ANSWER to "are you sure?" counts as yes, and the
        /// rejection is applied immediately. Set lower: they have already said it once and been asked
        /// point-blank, so the bar for the confirming word is not the bar for the first statement.
        /// </summary>
        public const float DeclineConfirmedScoreThreshold = 55f;

        // --- Acceptance thresholds (see MerchantTurnResult.AcceptanceScore) ---------
        //
        // The exact acceptance mirror of the withdrawal thresholds above. The model MEASURES how clearly
        // the player is agreeing to sell at the standing offer; the game DECIDES here. Choosing Accept was
        // as unreliable as choosing PlayerDeclined — the haggling personality keeps the merchant
        // negotiating even after the player says yes — so a high score is thresholded and coerced through
        // a one-turn confirmation, exactly like a withdrawal.

        /// <summary>
        /// Score at or above which an unprompted statement counts as the player agreeing to sell, and the
        /// merchant must stop and ask them to confirm. Set high (like <see cref="WithdrawalScoreThreshold"/>):
        /// mistaking ordinary haggling for agreement closes a sale the player did not mean to close.
        /// </summary>
        public const float AcceptanceScoreThreshold = 70f;

        /// <summary>
        /// Score at or above which the player's ANSWER to "are you sure you'll sell?" counts as yes, and
        /// the sale is closed immediately. Set lower (like <see cref="DeclineConfirmedScoreThreshold"/>):
        /// they have already agreed once and been asked point-blank.
        /// </summary>
        public const float AcceptConfirmedScoreThreshold = 55f;

        // Deterministic lines used ONLY when a turn is coerced to a different action than the model
        // spoke (a last-resort fallback after the corrected retry still produced an illegal offer). The
        // model's own line would contradict the coerced action (e.g. promise a raise on a HoldFirm — the
        // exact "HoldFirm but the price went up" bug), so we replace it with a matching line. Plain
        // colloquial Egyptian, no diacritics, numbers spelled out — safe to feed straight to the TTS.
        private const string CoercedHoldArabic = "لا يا ريس، ده اخر سعر عندي ومش هقدر ازود اكتر من كده.";
        private const string CoercedHoldEnglish = "No sir, that's my final price — I can't go any higher than this.";
        private const string CoercedUltimatumArabic = "يا ريس، ده اخر سعر ونهائي، يا تقبل يا نسيبها، قرر دلوقتي.";
        private const string CoercedUltimatumEnglish = "Sir, this is my final, last price — take it or leave it, decide now.";
        private const string CoercedWalkAwayArabic = "خلاص يا ريس، مفيش اتفاق النهاردة، ربنا يوفقك في السوق.";
        private const string CoercedWalkAwayEnglish = "That's it, sir — no deal today, good luck in the market.";
        private const string CoercedBargainAcceptArabic = "ماشي يا ريس، اتفقنا خلاص، هدهالك بالسعر اللي انت طالبه.";
        private const string CoercedBargainAcceptEnglish = "Alright sir, deal — I'll take it at the price you asked.";

        // Scripted line used when the player calls the sale off but has not been asked to confirm yet.
        // Ending a negotiation is destructive and a mis-transcription must never trigger it, so an
        // unconfirmed PlayerDeclined is always coerced into this one-turn confirmation question.
        private const string CoercedConfirmDeclineArabic =
            "استنى يا ريس، انت متاكد انك مش عايز تبيعلي خالص؟ قولي اه ولا لا.";
        private const string CoercedConfirmDeclineEnglish =
            "Hold on, sir — are you sure you don't want to sell to me at all? Tell me yes or no.";

        // Scripted farewell for the coerced decline: the merchant has already spent its one confirmation
        // and must not ask again, so the withdrawal it was told about is simply applied.
        private const string CoercedDeclineFarewellArabic =
            "ماشي يا ريس، زي ما تحب، مفيش بيع النهاردة، ربنا يوفقك.";
        private const string CoercedDeclineFarewellEnglish =
            "As you wish, sir — no sale today. Good luck to you.";

        // Acceptance mirror of the decline confirm line. The player agreed to sell, so before the sale is
        // locked in the merchant asks them to CONFIRM (one turn). Deliberately names no number — the price
        // is already on the table — so it stays a simple yes/no question, exactly like the decline confirm.
        private const string CoercedConfirmAcceptArabic =
            "طيب يا ريس، يبقى اتفقنا وهتبيعهالي؟ قولي اه ولا لا عشان اكد البيعة.";
        private const string CoercedConfirmAcceptEnglish =
            "Alright sir — so we're agreed and you'll sell it to me? Tell me yes or no so I can confirm the sale.";

        // Scripted closing line for the coerced acceptance: the merchant has already spent its one
        // confirmation, so the agreement it was told about is simply applied and the deal is warmly closed.
        private const string CoercedAcceptFarewellArabic =
            "تمام يا ريس، اتفقنا، البيعة ليا، الله يبارك لك في رزقك.";
        private const string CoercedAcceptFarewellEnglish =
            "Great, sir — it's a deal, the sale's mine. God bless your livelihood.";

        // Scripted WARNING spoken when the merchant tries to walk away without having told the player it
        // is leaving. It announces the departure plainly and gives them one turn to decide; only then may
        // a WalkAway actually end the negotiation.
        private const string CoercedFinalWarningArabic =
            "بص يا ريس، صبري خلص وانا همشي، دي اخر فرصة، يا نتفق دلوقتي يا كل واحد يروح في حاله.";
        private const string CoercedFinalWarningEnglish =
            "Look sir, my patience is finished and I'm about to leave. This is your last chance — we agree now, or we each go our own way.";

        // Scripted clarify lines used when the merchant tried to price the fish before the fisherman
        // told it what it needs. The merchant must know BOTH the species AND the weight before any
        // price talk; if it quotes early we coerce the turn to ASK for whatever is still missing.
        private const string CoercedAskFishArabic = "استنى يا ريس، وقلي الاول بتبيع سمك ايه بالظبط؟ لازم اعرف نوعه قبل ما اسعر.";
        private const string CoercedAskFishEnglish = "Hold on sir — first tell me exactly what fish you're selling? I need the type before I price it.";
        private const string CoercedAskWeightArabic = "وكام كيلو يا ريس تقريبا؟ لازم اعرف وزنها الاول عشان اقولك على سعر.";
        private const string CoercedAskWeightEnglish = "And how many kilos roughly, sir? I need the weight first before I can quote a price.";
        private const string CoercedAskBothArabic = "قلي الاول بتبيع سمك ايه وكام كيلو، وبعد كده نتكلم في الفلوس.";
        private const string CoercedAskBothEnglish = "First tell me what fish you're selling and how many kilos — then we'll talk money.";

        // Fallback description of the speech engine for the prompt when the TTS pool can't be
        // queried (manager missing / nothing available). Kept generic but truthful.
        private const string FallbackTtsDescription = "a native Egyptian-Arabic ElevenLabs text-to-speech voice";

        // The Egyptian-Arabic constraint from docs/MERCHANT_CONTEXT.md. It MUST appear in EVERY
        // prompt (LLMs drift toward MSA by default), so it lives here and is injected each turn.
        // Built per call (not a const) so the prompt names the EXACT TTS engine that will speak
        // the reply — resolved live from the injected TTS service (e.g. "ElevenLabs TTS (eleven_multilingual_v2)").
        private string BuildDialectInstruction()
        {
            string ttsName = _tts != null ? _tts.ActiveModelName : null;
            string ttsDescription = string.IsNullOrEmpty(ttsName)
                ? FallbackTtsDescription
                : $"a NATIVE Egyptian-Arabic text-to-speech engine, specifically \"{ttsName}\"";

            return
            "Respond only in Egyptian Arabic dialect (اللهجة المصرية العامية), not Modern Standard " +
            "Arabic. Speak like a real Damietta / El Borg fish-market vendor: warm, quick, casual " +
            "street banter (e.g. 'يا ريس', 'يا معلم', 'والله العظيم'), never formal or written Arabic.\n" +
            "CRITICAL OUTPUT FORMAT — your \"merchantSpeech\" is NOT read by a human. It is fed DIRECTLY, " +
            $"character for character, into {ttsDescription}. " +
            "Because a TTS engine speaks it literally:\n" +
            "1) Write EVERY word of merchantSpeech in ARABIC SCRIPT ONLY — Arabic letters exclusively. " +
            "NEVER use Latin/English letters or romanized transliteration: write 'يا ريس' NOT 'ya riis', " +
            "'كيلو' NOT 'kilo', 'اوكي' NOT 'ok'. Greetings, names and loan-words ALL go in Arabic letters. " +
            "A single Latin letter makes the TTS mispronounce or skip the word.\n" +
            "2) Write ALL numbers (prices, weights) as spelled-out Egyptian Arabic WORDS, never digits or " +
            "numerals — e.g. 'تسعين جنيه', not '90 جنيه' and not '٩٠' — so the voice pronounces them " +
            "naturally in Arabic, not in English.\n" +
            "3) Write COMPLETELY UNVOWELIZED, bare Arabic. Absolutely NO diacritics / tashkeel / harakat of " +
            "ANY kind — no fatha (  َ  ), damma (  ُ  ), kasra (  ِ  ), shadda (  ّ  ), sukun (  ْ  ), tanwin " +
            "(  ً   ٌ   ٍ  ) or superscript marks. Vowelization marks make the TTS mispronounce the words. " +
            "Every Arabic letter must be plain, exactly the way Egyptians type in an everyday chat message.\n" +
            "4) PUNCTUATE for natural speech pacing — the TTS uses punctuation to decide where to pause, so " +
            "never return one flat run-on string. Put an Arabic comma (،) where the voice should pause " +
            "briefly, a full stop (.) to end a sentence with a longer pause, an ellipsis (...) for a " +
            "trailing / thinking pause or hesitation, and a question mark (؟) when you ask something. " +
            "Separate multiple sentences with these marks so the delivery sounds natural, not robotic.\n" +
            "PRIORITY: merchantSpeech must be FLAWLESS Egyptian Arabic script (it is spoken aloud). The " +
            "\"speechTranslationEnglish\" field is ONLY subtitles — minor English mistakes there are " +
            "tolerated, so put your effort into getting the Arabic 100% right first.";
        }

        /// <summary>
        /// Runs one negotiation turn through the LLM and returns a validated
        /// <see cref="MerchantTurnResult"/>, or null if it could not be produced (in which case a
        /// <see cref="LLMResponseFailedEvent"/> has already been published). Tries a full prompt
        /// first, then a simplified prompt once, before giving up.
        /// </summary>
        /// <param name="isDeceptive">
        /// Whether THIS merchant is being deceptive this turn, as decided by
        /// <see cref="PriceNegotiationCalculator"/>. The model expresses it in tone only.
        /// </param>
        public async Task<MerchantTurnResult> ProcessPlayerTurn(
            NegotiationState state, string playerSpeechText, bool isDeceptive)
        {
            if (state == null || state.currentMerchant == null || state.fishBeingSold == null)
            {
                FailTurn("Incomplete negotiation state.");
                return null;
            }

            if (_llm == null || !_llm.HasAvailableModel)
            {
                FailTurn("No LLM model available.");
                return null;
            }

            // Attempt 1: full prompt, STRICT — an offer-rule violation is REJECTED (not coerced) so the
            // model's spoken line always matches what the game applies. On failure we retry with the
            // exact reason, giving the model a chance to produce a clean, in-character line itself.
            string reply = await SendPromptAsync(BuildPrompt(state, playerSpeechText, isDeceptive, simplified: false));
            if (TryParseAndValidate(reply, state, allowCoercion: false, out MerchantTurnResult result, out string rejectionReason))
            {
                CommitTurn(state, playerSpeechText, result);
                return result;
            }

            // Attempt 2: simplified + corrected prompt (feeds back WHY attempt 1 was rejected), and this
            // time COERCION is allowed — if the model STILL proposes an illegal offer, we coerce it to a
            // legal action AND swap in a matching deterministic line, so the turn never dead-ends into a
            // "say that again" AND a coerced HoldFirm/WalkAway never contradicts its own speech.
            Debug.LogWarning($"MerchantNegotiationLLM: first attempt invalid ({rejectionReason}); " +
                             "retrying with a corrected prompt.");
            // Keep the personality on the retry (retries are now mostly OFFER corrections, where an
            // in-character line matters); the correction hint is what steers the fix.
            reply = await SendPromptAsync(BuildPrompt(state, playerSpeechText, isDeceptive, simplified: false, correctionHint: rejectionReason));
            if (TryParseAndValidate(reply, state, allowCoercion: true, out result, out _))
            {
                CommitTurn(state, playerSpeechText, result);
                return result;
            }

            FailTurn("LLM returned no valid structured response after retry.");
            return null;
        }

        // Records the exchange into the transcript AND appends the player's reported ask (if any) to
        // the running ask history, so later turns can notice a raised ask.
        private static void CommitTurn(NegotiationState state, string playerSpeechText, MerchantTurnResult result)
        {
            RecordExchange(state, playerSpeechText, result.MerchantSpeech);

            // Decline bookkeeping. Asking "are you sure?" arms the ONE turn on which the rejection score is
            // read as a yes/no answer; any other action means the player carried on haggling, so disarm.
            // The merchant can never ask twice in a row: ConfirmDecline is only ever produced while this
            // flag is CLEAR, and producing it sets the flag.
            if (result.Action == MerchantAction.ConfirmDecline)
                state.awaitingDeclineConfirmation = true;
            else if (result.Action != MerchantAction.PlayerDeclined)
                state.awaitingDeclineConfirmation = false;

            // Acceptance bookkeeping — the exact mirror of the decline bookkeeping above. Asking "are you
            // sure you'll sell?" arms the ONE turn on which the acceptance score is read as a yes/no answer;
            // any other action means the player carried on haggling, so disarm. ConfirmAccept is only ever
            // produced while this flag is CLEAR, so the merchant can never ask twice in a row.
            if (result.Action == MerchantAction.ConfirmAccept)
                state.awaitingAcceptConfirmation = true;
            else if (result.Action != MerchantAction.PlayerAccepted)
                state.awaitingAcceptConfirmation = false;

            // The merchant has now TOLD the player it is about to leave — either the coerced warning
            // above, or the hold-at-max ULTIMATUM turn (turnsHeldAtMax is still the pre-turn value here,
            // the same one BuildPrompt used to demand the ultimatum). Only after this may a WalkAway end
            // the negotiation. Once warned, it stays warned for the rest of the haggle.
            if (result.Action == MerchantAction.HoldFirm
                && (result.IsFinalWarning || state.turnsHeldAtMax >= NegotiationState.HoldsBeforeUltimatum))
                state.walkAwayWarningGiven = true;

            // Accumulate what the fisherman has disclosed so later turns can't "forget" the fish/weight
            // (and so the sale can be saved). Once known it stays known — a blank later report never clears it.
            if (!string.IsNullOrWhiteSpace(result.UnderstoodFishType))
                state.disclosedFishType = result.UnderstoodFishType;
            if (result.UnderstoodWeightKg > 0f)
                state.disclosedWeightKg = result.UnderstoodWeightKg;

            if (result.PlayerAskedPrice > 0f)
            {
                if (state.playerAskHistory == null) state.playerAskHistory = new List<float>();
                float ask = Mathf.Round(result.PlayerAskedPrice);
                // The model reports the STANDING ask each turn; only record it when it actually changes
                // so the history stays a clean list of distinct asks (e.g. 150, 190 — not 150,150,190).
                int count = state.playerAskHistory.Count;
                if (count == 0 || state.playerAskHistory[count - 1] != ask)
                    state.playerAskHistory.Add(ask);
            }
        }

        /// <summary>
        /// Generates the merchant's OPENING line: a greeting that states the deterministic opening
        /// offer (already set on <paramref name="state"/>.currentOffer by
        /// <see cref="PriceNegotiationCalculator"/>). The model phrases the line only — it does NOT
        /// invent the number. Returns a validated <see cref="MerchantTurnResult"/> (its Action is a
        /// neutral placeholder the caller ignores for the opening) or null on unrecoverable failure.
        /// </summary>
        /// <param name="withGreeting">
        /// When true the opening line opens with an Egyptian-style greeting; when false the merchant
        /// skips pleasantries and goes straight to sizing up the fish + price (they already know the
        /// fisherman). The caller decides this (always greet the first conversation; sometimes after).
        /// </param>
        public async Task<MerchantTurnResult> GenerateOpeningLine(NegotiationState state, bool isDeceptive, bool withGreeting = true)
        {
            if (state == null || state.currentMerchant == null || state.fishBeingSold == null)
            {
                FailTurn("Incomplete negotiation state (opening line).");
                return null;
            }

            if (_llm == null || !_llm.HasAvailableModel)
            {
                FailTurn("No LLM model available (opening line).");
                return null;
            }

            string reply = await SendPromptAsync(BuildOpeningPrompt(state, isDeceptive, withGreeting, simplified: false));
            if (TryParseAndValidate(reply, state, allowCoercion: true, out MerchantTurnResult result, out _))
            {
                RecordExchange(state, null, result.MerchantSpeech); // opening: merchant speaks first
                return result;
            }

            Debug.LogWarning("MerchantNegotiationLLM: opening line invalid; retrying with simplified prompt.");
            reply = await SendPromptAsync(BuildOpeningPrompt(state, isDeceptive, withGreeting, simplified: true));
            if (TryParseAndValidate(reply, state, allowCoercion: true, out result, out _))
            {
                RecordExchange(state, null, result.MerchantSpeech); // opening: merchant speaks first
                return result;
            }

            FailTurn("LLM returned no valid opening line after retry.");
            return null;
        }

        // --- Transport bridge: callback-based GeminiClient -> awaitable ------------

        // Returns the model's raw reply text, or null on failure. The LLMManager picks an available
        // model and falls back across the pool; the callback->Task bridge now lives in each model
        // (e.g. GeminiClient.GenerateTextAsync). Resolves on the main thread, so the await does too.
        private Task<string> SendPromptAsync(string prompt)
        {
            if (_llm == null) return Task.FromResult<string>(null);
            // jsonObject: force the provider into structured-output mode (Groq json_object / Gemini
            // application/json) so the reply is a single valid JSON object, not prose we then fail to
            // parse and recover from.
            return _llm.GenerateTextAsync(prompt, jsonObject: true);
        }

        // --- Prompt construction ---------------------------------------------------

        private string BuildPrompt(NegotiationState state, string playerSpeechText, bool isDeceptive, bool simplified,
            string correctionHint = null)
        {
            MerchantConfig merchant = state.currentMerchant;
            FishData fish = state.fishBeingSold;
            int patienceRemaining = Mathf.Max(0, merchant.PatienceLevel - state.turnCount);
            string playerSaid = string.IsNullOrWhiteSpace(playerSpeechText) ? "(unclear / no speech)" : playerSpeechText;

            var sb = new StringBuilder();

            // Retry correction: the previous reply was rejected for breaking a game rule (almost always
            // an illegal offer). Lead with it so the model fixes THAT specific mistake instead of
            // repeating it — this is what stops a merchant stuck at its ceiling from failing every turn.
            if (!string.IsNullOrWhiteSpace(correctionHint))
            {
                sb.AppendLine("!!! YOUR PREVIOUS REPLY WAS REJECTED — DO NOT REPEAT THAT MISTAKE.");
                sb.AppendLine($"Reason: {correctionHint}");
                sb.AppendLine("Pick an action that obeys the pricing rules below. If you cannot legally raise " +
                              "your offer, then HoldFirm, Accept, or WalkAway instead — never propose a number " +
                              "you are told you may not.");
                sb.AppendLine();
            }

            sb.AppendLine("You are an NPC fish MERCHANT (a buyer) at the El Borg fish auction market in Dumyat (Damietta), Egypt.");
            sb.AppendLine("The player is a FISHERMAN who just caught a fish and walked up to SELL it to you — YOU are buying it from them.");
            sb.AppendLine("You ONLY buy and haggle over THIS one fish. Nothing else is for sale and the player cannot buy anything from you. " +
                          "If they say anything off-topic, steer them back to selling this fish (use ClarifyRequest).");
            sb.AppendLine(BuildDialectInstruction());
            sb.AppendLine();

            if (!simplified)
            {
                sb.AppendLine("PERSONALITY (stay in character as this specific vendor):");
                sb.AppendLine(merchant.Personality);
                sb.AppendLine();
            }

            // Deception is decided by game logic, not the model. We only tell it the TONE to take.
            sb.AppendLine(isDeceptive
                ? "TONE THIS TURN: you are quietly trying to get a bargain (lowball) — be warm and persuasive, " +
                  "not openly dishonest or villainous. Never admit you are lowballing."
                : "TONE THIS TURN: you are dealing fairly and openly this turn.");
            sb.AppendLine();

            // Replay the whole conversation so the (stateless) model stays consistent with what it
            // and the player have already said and offered, instead of treating each turn as new.
            AppendTranscript(sb, state);

            string catalogText = BuildFishCatalog(state);

            bool fishDisclosed = !string.IsNullOrWhiteSpace(state.disclosedFishType);
            bool weightDisclosed = state.disclosedWeightKg > 0f;

            sb.AppendLine("WHAT YOU KNOW:");
            sb.AppendLine("- You do NOT automatically know what fish this is or how much it weighs. NEVER assume, " +
                          "guess or name a fish yourself. You learn the fish AND its weight ONLY from what the " +
                          "fisherman tells you in the conversation above. Until they have clearly told you BOTH " +
                          "(a) which fish they caught AND (b) roughly how much it weighs, ASK for whatever is " +
                          "still missing (ClarifyRequest) before quoting any price — never invent a fish or a " +
                          "weight they did not say.");
            sb.AppendLine("- CRITICAL: a WEIGHT tells you NOTHING about the species — many different fish weigh the " +
                          "same. Knowing how many kilos it is does NOT let you name the fish. If the fisherman gave " +
                          "you a weight but has NOT named the fish, you STILL do not know the fish: you MUST ask which " +
                          "fish it is (ClarifyRequest). Likewise if they named the fish but not the weight, ask the weight.");
            sb.AppendLine("- SO FAR THE FISHERMAN HAS EXPLICITLY TOLD YOU:");
            sb.AppendLine($"    • Fish type: {(fishDisclosed ? state.disclosedFishType : "NOT told yet — you do NOT know the fish; do not price it, ASK.")}");
            sb.AppendLine($"    • Weight: {(weightDisclosed ? state.disclosedWeightKg.ToString("0.##") + " kg" : "NOT told yet — you do NOT know the weight; do not price it, ASK.")}");
            if (catalogText != null)
            {
                sb.AppendLine("- This market ONLY buys the following fish. The list is what you are WILLING to buy — it " +
                              "is NOT a hint about which fish the fisherman brought; never guess their fish from it or " +
                              "from the weight. Acknowledge a fish ONLY once THEY name it AND it is on this list; if " +
                              "they name anything else, tell them (in character) you don't deal that fish here and ask " +
                              "for one you do (ClarifyRequest) — never price an unknown fish:");
                sb.Append(catalogText);
                sb.AppendLine("- WEIGHT LIMITS ARE STRICT: you can only buy a fish whose weight is WITHIN the band " +
                              "shown for the species the fisherman named. If their stated weight is BELOW the " +
                              "minimum or ABOVE the maximum for that fish, you CANNOT buy it — say so plainly (too " +
                              "small / too big for you to take) and do not quote a price (ClarifyRequest, or " +
                              "WalkAway if they insist).");
            }
            else
            {
                sb.AppendLine($"- ONLY once the fisherman has told you the fish, refer to it by its correct " +
                              $"Egyptian name: {DescribeFish(fish)}");
            }
            sb.AppendLine();
            float ceiling = NegotiationCeiling(state);
            bool atCeiling = state.currentOffer >= ceiling;

            sb.AppendLine("YOUR PRICING (all amounts are in Egyptian Pounds, whole numbers only):");
            sb.AppendLine($"- The price you are currently offering to pay: {EGP(state.currentOffer)}");
            sb.AppendLine($"- The ABSOLUTE MAXIMUM you will EVER pay for this fish: {EGP(ceiling)}. This is your " +
                          "hard ceiling for this whole negotiation — private, never reveal it, NEVER offer above it.");
            sb.AppendLine($"- Fair market value (your private reference — do NOT reveal this number to the player): {EGP(fish.fairMarketValue)}");
            sb.AppendLine($"- Turn number: {state.turnCount}");
            sb.AppendLine($"- Your patience remaining (counter-offers left before you walk away): {patienceRemaining}");
            sb.AppendLine($"- Player just said (transcribed Egyptian speech): \"{playerSaid}\"");

            // Ask history: let the merchant notice a seller who RAISES their own asking price mid-haggle.
            AppendAskHistory(sb, state);
            sb.AppendLine();

            float raiseCap = RaiseCap(state);

            sb.AppendLine("HOW YOU HAGGLE (offers move in ONE direction only — UP, and only a little at a time):");
            sb.AppendLine("- You are the BUYER: you start low and can be argued UPWARD toward your maximum. You " +
                          "NEVER lower an offer you have already made — a RaiseOffer's newOfferAmount MUST be " +
                          $"HIGHER than your current {EGP(state.currentOffer)} and NEVER above your maximum {EGP(ceiling)}.");
            sb.AppendLine($"- YOUR OFFER IS A RATCHET — it only ever goes UP or stays the SAME, never down. You have " +
                          $"already offered {EGP(state.currentOffer)}; every price you now SAY OUT LOUD must be " +
                          $"{EGP(state.currentOffer)} OR MORE. Never speak, hint at, or 'go back to' any number " +
                          $"below {EGP(state.currentOffer)} — you cannot un-offer money you already put on the table. " +
                          "If you are not raising, hold at exactly your current offer.");
            sb.AppendLine("- NEVER PAY MORE THAN THE FISHERMAN ASKS. You are buying — paying above their price " +
                          "is throwing money away. Look at the price the fisherman is asking for RIGHT NOW:\n" +
                          "   • If their asked price is AT OR BELOW your current offer → Accept at THEIR price " +
                          "(BARGAIN ACCEPT below), do not counter higher.\n" +
                          "   • If their asked price is ABOVE your current offer but AT OR BELOW your maximum → " +
                          "you can afford it: either Accept AT their asked price, or try to pay LESS by holding " +
                          "firm / raising to a number STRICTLY BELOW what they asked. A RaiseOffer must NEVER " +
                          "meet or exceed the price they asked — if you'd match it, just Accept at their price " +
                          "instead.\n" +
                          "   • If their asked price is ABOVE your maximum → you cannot reach it: HoldFirm near " +
                          "your maximum or WalkAway. Never exceed your maximum to chase their number.");
            sb.AppendLine($"- Concede GRADUALLY: if you raise THIS TURN, newOfferAmount must be between " +
                          $"{EGP(state.currentOffer + 1f)} and {EGP(raiseCap)}. Never jump straight to your " +
                          "maximum — small, believable market steps, and you do NOT have to reach your maximum.");
            sb.AppendLine("- If the player has only just told you what fish they have and no price talk has " +
                          $"happened yet, QUOTE your current offer {EGP(state.currentOffer)} in your speech " +
                          "(action HoldFirm) — the haggle is just beginning.");
            sb.AppendLine("- ANSWERING A PLAIN QUESTION: if the fisherman is just ASKING you something rather than " +
                          "haggling — most commonly they forgot and ask what price you offered ('انت قلت كام؟', " +
                          "'السعر كام تاني؟'), or ask you to repeat what fish/weight you meant — simply ANSWER it. " +
                          $"Restate the fact they asked for (for a price question, your current offer of " +
                          $"{EGP(state.currentOffer)}) in a plain speech-only turn (action HoldFirm) and do NOT " +
                          "change your offer. A question is NOT an offer, an agreement, or a reason to move your " +
                          "price: never RaiseOffer, Accept, or lower anything just because you were asked something.");
            if (state.turnsHeldAtMax >= NegotiationState.HoldsBeforeWalkAway)
                sb.AppendLine($"- WALK AWAY NOW: you already gave the player your final ultimatum and they STILL " +
                              "will not decide — they keep haggling instead of accepting or declining. You are done: " +
                              "choose WalkAway with ONE short, firm but polite line that ENDS the deal (my final word, " +
                              "no deal, good luck elsewhere). Do NOT HoldFirm again. Do NOT RaiseOffer.");
            else if (state.turnsHeldAtMax >= NegotiationState.HoldsBeforeUltimatum)
                sb.AppendLine($"- ULTIMATUM TURN — you have defended your maximum {EGP(state.currentOffer)} several " +
                              "times and will NOT haggle any longer. Do NOT RaiseOffer. Choose HoldFirm, but make it a " +
                              "STRICT, final ULTIMATUM: tell the player plainly this is your absolute final price and " +
                              "they must DECIDE RIGHT NOW — either accept it ('قول اتفقنا وخدها') or the deal is off. " +
                              "Make clear you will not go higher and won't keep haggling. Do NOT walk away yet — give " +
                              "them THIS turn to decide (Accept if they agree).");
            else if (atCeiling)
                sb.AppendLine("- YOU ARE AT YOUR MAXIMUM RIGHT NOW: do NOT use RaiseOffer again. HoldFirm (firmly urge " +
                              "them to take this good price) or Accept if they agree. The player may keep trying — " +
                              "keep holding firm for now, and never leave without first warning them.");
            else
                sb.AppendLine("- If the player demands MORE than your maximum, do not exceed it: HoldFirm near your " +
                              "ceiling (make clear you're near your limit). When your patience runs out, WARN them " +
                              "you are about to leave — do not leave in the same breath.");
            sb.AppendLine($"- BARGAIN ACCEPT (snap it up): you are the BUYER, so if the fisherman ASKS FOR a " +
                          $"price that is AT OR BELOW your current offer of {EGP(state.currentOffer)}, you " +
                          "IMMEDIATELY Accept at THEIR price — never turn down paying less. This counts as " +
                          "acceptance, NOT haggling: do NOT hold firm or try to renegotiate a cheaper deal " +
                          "you were already handed. Warmly close it and reassure them it's a good deal so " +
                          "they don't back out. \"newOfferAmount\" = the fisherman's asked price.");
            sb.AppendLine();

            sb.AppendLine("CLOSING THE DEAL — Accept ENDS the sale, so be VERY strict:");
            sb.AppendLine($"- AGREEMENT ACCEPT: the fisherman EXPLICITLY agrees to sell at your CURRENT offer " +
                          $"with clear deal words ('اتفقنا', 'خلاص موافق', 'ماشي خدها') and NO new number " +
                          $"attached. Then newOfferAmount = {EGP(state.currentOffer)}. A price ABOVE your " +
                          $"current offer of {EGP(state.currentOffer)} is haggling — NOT acceptance (but a " +
                          "price at/below it is the BARGAIN ACCEPT above — take it).");
            sb.AppendLine();

            AppendRejectionScoreRules(sb, state);
            AppendAcceptanceScoreRules(sb, state);

            // Walking away ends the sale too, and the player must always hear it coming.
            if (state.walkAwayWarningGiven)
                sb.AppendLine("- YOU HAVE ALREADY WARNED the fisherman that you are about to leave. If they still " +
                              "will not accept or decline, choose WalkAway now and say ONE short, firm farewell " +
                              "that plainly ENDS the deal (no deal today, good luck elsewhere).");
            else
                sb.AppendLine("- NEVER WALK AWAY WITHOUT WARNING. However little patience you have left, you may not " +
                              "just leave: you must FIRST spend a turn telling the fisherman plainly that your " +
                              "patience is finished and you are about to walk, giving them one last chance to " +
                              "decide (action HoldFirm, with that warning spoken out loud). Only on a LATER turn, " +
                              "if they still will not decide, may you use WalkAway.");
            sb.AppendLine();

            sb.AppendLine("- DEFAULT WHEN UNSURE: keep negotiating (HoldFirm / RaiseOffer / ClarifyRequest). " +
                          "Ending the sale by mistake ruins the game round; treat Accept and PlayerDeclined as " +
                          "rare, definitive endings you only pick on unmistakable words.");
            sb.AppendLine();

            sb.AppendLine("Return your reply as exactly one valid json object and nothing else. Schema:");
            sb.AppendLine("{");
            sb.AppendLine("  \"merchantSpeech\": \"<what you say out loud, in Egyptian dialect>\",");
            sb.AppendLine("  \"speechTranslationEnglish\": \"<English translation of merchantSpeech, for subtitles>\",");
            sb.AppendLine("  \"action\": \"RaiseOffer\" | \"HoldFirm\" | \"Accept\" | \"WalkAway\" | \"ClarifyRequest\",");
            sb.AppendLine("  \"rejectionScore\": <integer 0-100 — see REJECTION SCORE above. REQUIRED every turn. " +
                          "Use 0 when the fisherman shows no sign at all of ending the sale>,");
            sb.AppendLine("  \"acceptanceScore\": <integer 0-100 — see ACCEPTANCE SCORE above. REQUIRED every turn. " +
                          "Use 0 when the fisherman shows no sign at all of agreeing to sell at your offer>,");
            sb.AppendLine("  \"playerAskedPrice\": <number — the price in EGP the fisherman is CURRENTLY asking " +
                          "you to pay (the most recent price they named or insisted on, even if a few turns " +
                          "back and still standing). Whole number. Use 0 if they have not named any price yet " +
                          "or it is unclear. This is the SELLER's price, NOT your offer>,");
            sb.AppendLine("  \"understoodFishType\": \"<the fish species the fisherman has EXPLICITLY named so far, " +
                          "in Arabic. EMPTY STRING if they have not named a fish yet. NEVER guess it from the weight " +
                          "or the catalog — only fill this when they actually said which fish it is>\",");
            sb.AppendLine("  \"understoodWeightKg\": <number — the weight in kg the fisherman has EXPLICITLY stated. " +
                          "0 if they have not stated a weight yet>,");
            sb.AppendLine("  \"newOfferAmount\": <number, REQUIRED only when action is RaiseOffer or Accept; omit otherwise>");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("Use \"ClarifyRequest\" if the player's speech was unclear or unrelated, and ask them " +
                          "(in character) to repeat or get back to the fish.");
            sb.AppendLine();

            sb.AppendLine("HARD RULES:");
            sb.AppendLine("- Output ONLY the JSON object. No markdown, no code fences, no text before or after it.");
            sb.AppendLine("- NEVER break character. NEVER mention being an AI, a model, or a game.");
            sb.AppendLine("- ONLY discuss this fish sale. If the player goes off-topic, redirect in character via ClarifyRequest.");
            sb.AppendLine("- You CANNOT end the sale because the fisherman wants to leave — you have no action for " +
                          "it. Report what you heard in \"rejectionScore\" and keep negotiating; the market handles " +
                          "the rest. NEVER argue that they cannot refuse, and never say 'there is no such thing as " +
                          "not wanting to sell'.");
            sb.AppendLine("- NEVER use WalkAway unless you have ALREADY spent an earlier turn warning the fisherman " +
                          "out loud that you are about to leave. Leaving without that warning is forbidden — warn " +
                          "first (HoldFirm), walk second.");
            sb.AppendLine("- NEVER quote, offer, raise, or accept ANY price until the fisherman has EXPLICITLY told you " +
                          "BOTH which fish it is AND its weight. A weight alone does NOT identify the fish — never " +
                          "guess the species from the weight or the catalog. If either is still missing, use " +
                          "ClarifyRequest and ask for the missing one — do NOT put a number on an unknown fish.");
            sb.AppendLine($"- RaiseOffer: newOfferAmount MUST be STRICTLY HIGHER than {EGP(state.currentOffer)} " +
                          $"and AT MOST {EGP(raiseCap)} this turn. Never lower a standing offer; never exceed " +
                          "your maximum; never jump more than this turn's cap.");
            sb.AppendLine("- A QUESTION IS NOT A HAGGLE: if the fisherman merely ASKS you something (e.g. what price " +
                          "you offered, or to repeat the fish/weight), just ANSWER it with a speech-only HoldFirm " +
                          $"that restates your current offer {EGP(state.currentOffer)} unchanged. NEVER RaiseOffer, " +
                          "Accept, or move your price in response to a question.");
            sb.AppendLine($"- THE SPOKEN PRICE IS BINDING AND NEVER DROPS: the number in merchantSpeech must NEVER " +
                          $"be below your current offer {EGP(state.currentOffer)}. On HoldFirm, if you name a price " +
                          $"it must be EXACTLY {EGP(state.currentOffer)}; on RaiseOffer, speak EXACTLY your new " +
                          "(higher) newOfferAmount. Saying a smaller number than you already offered is forbidden.");
            sb.AppendLine("- NEVER offer or agree to MORE than the fisherman just asked for. If a raise would " +
                          "meet or beat their asked price, Accept at their asked price instead — you are the " +
                          "buyer and must never volunteer to pay more than you were asked.");
            sb.AppendLine("- Keep merchantSpeech to one or two short, natural market-style sentences.");
            sb.AppendLine("- Every price is in Egyptian Pounds (بالجنيه المصري) and MUST be a WHOLE number — " +
                          "never decimals, fractions or piastres (say 'تسعين جنيه', never 'تسعين جنيه ونص'). " +
                          "\"newOfferAmount\" must be a whole integer.");
            sb.AppendLine("- CRITICAL: in merchantSpeech, spell every number out as Egyptian WORDS " +
                          "(never digits like 90 or ٩٠), and write plain Arabic with NO diacritics / tashkeel / " +
                          "vocalization (harakat). The English translation may keep normal digits.");

            return sb.ToString();
        }

        /// <summary>
        /// Teaches the model the "rejectionScore" contract. It is framed as an OBSERVATION about the
        /// fisherman, deliberately divorced from the merchant's own choices — the merchant used to be
        /// asked to CHOOSE to end the sale, and its personality reliably talked it out of doing so
        /// ("مفيش حاجة اسمها مش عايز ابيع"). Rating what it just heard is not a decision it can be
        /// in character about, and the game does the deciding.
        /// </summary>
        private static void AppendRejectionScoreRules(StringBuilder sb, NegotiationState state)
        {
            sb.AppendLine("REJECTION SCORE — \"rejectionScore\" (0-100). This is a MEASUREMENT, not a decision.");
            sb.AppendLine("Rate ONLY what the fisherman just said. Never mention this number, never let it change " +
                          "your speech, and never end the negotiation yourself because of it — the market decides " +
                          "what happens next, not you. Score honestly even when you would rather keep haggling.");

            if (state.awaitingDeclineConfirmation)
            {
                sb.AppendLine("You have JUST asked the fisherman to confirm they want to call the sale off. Their " +
                              "words above are their ANSWER. Rate how clearly that answer is YES:");
                sb.AppendLine("  0-20  = clearly NO — they are still selling, they haggle on, they name a price, " +
                              "they say they were joking ('لا', 'كنت بهزر', 'طيب كام؟').");
                sb.AppendLine("  40-60 = unclear, unrelated, or an evasive answer.");
                sb.AppendLine("  80-100 = unmistakably YES — an affirmation ('اه', 'ايوه', 'اكيد', 'متاكد'), OR they " +
                              "simply repeat that they will not sell. Insisting IS confirming.");
                sb.AppendLine("Then answer this turn as if the haggle continues (HoldFirm / RaiseOffer / Accept / " +
                              "ClarifyRequest) — if they really did confirm, the market ends the sale for you and " +
                              "your line is not used.");
            }
            else
            {
                sb.AppendLine("Rate how clearly the fisherman is saying they no longer want to SELL THIS FISH AT ALL, " +
                              "as opposed to merely rejecting your price:");
                sb.AppendLine("  0-20  = ordinary haggling — refusing your price, calling it low, demanding more, " +
                              "insulting the offer ('لا ده قليل', 'مش كفاية', 'بتهزر؟'). This is NOT rejection.");
                sb.AppendLine("  40-60 = ambiguous — irritated, hinting they might leave, or the speech was garbled.");
                sb.AppendLine("  80-100 = unmistakable — they are done trading with you ('مش عايز ابيع', 'مش هبيع خلاص', " +
                              "'مش هكمل', 'مع السلامة', 'هدور على غيرك').");
                sb.AppendLine("Then answer this turn normally, as though the haggle continues. If they really are " +
                              "leaving, the market will stop and ask them to confirm — that is not your job.");
            }
            sb.AppendLine();
        }

        /// <summary>
        /// Teaches the model the "acceptanceScore" contract — the exact acceptance mirror of
        /// <see cref="AppendRejectionScoreRules"/>. Framed as an OBSERVATION about the fisherman, divorced
        /// from the merchant's own haggling instinct: choosing to close was as unreliable as choosing to
        /// end the sale (the personality keeps it negotiating), so the model only RATES how clearly the
        /// player is agreeing and the game does the deciding.
        /// </summary>
        private static void AppendAcceptanceScoreRules(StringBuilder sb, NegotiationState state)
        {
            sb.AppendLine("ACCEPTANCE SCORE — \"acceptanceScore\" (0-100). This is a MEASUREMENT, not a decision.");
            sb.AppendLine("Rate ONLY what the fisherman just said. Never mention this number, never let it change " +
                          "your speech, and never close the sale yourself because of it — the market decides what " +
                          "happens next, not you. Score honestly even when you would rather keep haggling for a " +
                          "cheaper price.");

            if (state.awaitingAcceptConfirmation)
            {
                sb.AppendLine("You have JUST asked the fisherman to confirm they agree to sell to you. Their words " +
                              "above are their ANSWER. Rate how clearly that answer is YES:");
                sb.AppendLine("  0-20  = clearly NO — they back out, they haggle on, they demand a different price, " +
                              "they say they changed their mind ('لا', 'استنى', 'عايز اكتر').");
                sb.AppendLine("  40-60 = unclear, unrelated, or an evasive answer.");
                sb.AppendLine("  80-100 = unmistakably YES — an affirmation ('اه', 'ايوه', 'اكيد', 'ماشي', 'اتفقنا', " +
                              "'تمام خدها'). Confirming the deal IS agreeing.");
                sb.AppendLine("Then answer this turn as if the haggle continues (HoldFirm / RaiseOffer / " +
                              "ClarifyRequest) — if they really did confirm, the market closes the sale for you and " +
                              "your line is not used.");
            }
            else
            {
                sb.AppendLine("Rate how clearly the fisherman, IN THE WORDS THEY JUST SAID THIS TURN, is agreeing to " +
                              "sell at your CURRENT offer. Judge ONLY this turn's words: a higher price they named on " +
                              "an EARLIER turn is stale and must NOT drag this score down — agreement is about what " +
                              "they say NOW, not the number still sitting in the ask history.");
                sb.AppendLine("  0-20  = still haggling THIS TURN — refusing your price, or naming a NEW higher price " +
                              "right now. (A price they demanded on a PREVIOUS turn does not count as haggling here.)");
                sb.AppendLine("  40-60 = leaning toward yes but not a clear commitment, or the speech was garbled.");
                sb.AppendLine("  80-100 = unmistakable agreement words THIS TURN ('اتفقنا', 'خلاص موافق', 'ماشي خدها', " +
                              "'تمام هبيعهالك', 'انا موافق', 'خلاص بيعتهالك'), OR they ask for a price AT OR BELOW your " +
                              "current offer. Score this HIGH even if they were demanding MORE only a moment ago and " +
                              "even if a higher ask is STILL on record — a clear 'موافق' NOW outranks any stale number, " +
                              "and the sale simply closes at YOUR current offer, never their earlier higher one.");
                sb.AppendLine("Then answer this turn normally, as though the haggle continues. If they really are " +
                              "agreeing, the market will stop and ask them to confirm — that is not your job.");
            }
            sb.AppendLine();
        }

        // Opening-line prompt: (optionally greet) + announce the FIXED opening offer. Same JSON schema
        // so the existing parser/validator is reused; action is pinned to HoldFirm (a no-op the caller
        // ignores) and the model is told the price is fixed so it can't move the number.
        private string BuildOpeningPrompt(NegotiationState state, bool isDeceptive, bool withGreeting, bool simplified)
        {
            MerchantConfig merchant = state.currentMerchant;

            var sb = new StringBuilder();

            sb.AppendLine("You are an NPC fish MERCHANT (a buyer) at the El Borg fish auction market in Dumyat (Damietta), Egypt.");
            sb.AppendLine("The player is a FISHERMAN who just caught a fish and walked up to SELL it to you — YOU are buying it from them.");
            sb.AppendLine("You ONLY buy and haggle over THIS one fish. Nothing else is for sale and the player cannot buy anything from you. " +
                          "If they say anything off-topic, steer them back to selling this fish (use ClarifyRequest).");
            sb.AppendLine(BuildDialectInstruction());
            sb.AppendLine();

            if (!simplified)
            {
                sb.AppendLine("PERSONALITY (stay in character as this specific vendor):");
                sb.AppendLine(merchant.Personality);
                sb.AppendLine();
            }

            sb.AppendLine(isDeceptive
                ? "TONE: you are quietly trying to get a bargain — warm and persuasive, never openly dishonest."
                : "TONE: you are dealing fairly and openly.");
            sb.AppendLine();

            sb.AppendLine("THIS IS YOUR OPENING LINE. A fisherman just walked up to you to sell their fresh catch.");
            sb.AppendLine("You do NOT yet know what they caught or how much it weighs — you can't see it clearly " +
                          "and they haven't told you. Do NOT name any fish, any weight, or any price yet.");
            sb.AppendLine(withGreeting
                ? "Open with a warm Egyptian market-style greeting (a friendly call like 'يا ريس' / " +
                  "'اهلا يا معلم' / 'يا باشا' / يا غالي'), then ask the fisherman what they've got for you today and roughly how " +
                  "much it weighs. One or two short, natural market sentences."
                : "Do NOT greet — you already know this fisherman and have dealt with them before. Skip any " +
                  "salutation and go straight to asking what they've brought you today and roughly how much " +
                  "it weighs, in natural Egyptian market style. One or two short sentences.");
            sb.AppendLine();

            sb.AppendLine("Return your reply as exactly one valid json object and nothing else. Schema:");
            sb.AppendLine("{");
            sb.AppendLine("  \"merchantSpeech\": \"<your opening line — greet (if appropriate) and ask what they caught; NO fish name, weight or price yet, in Egyptian dialect>\",");
            sb.AppendLine("  \"speechTranslationEnglish\": \"<English translation, for subtitles>\",");
            sb.AppendLine("  \"action\": \"HoldFirm\",");
            sb.AppendLine("  \"newOfferAmount\": 0");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine("HARD RULES:");
            sb.AppendLine("- Output ONLY the JSON object. No markdown, no code fences, no text before or after it.");
            sb.AppendLine("- NEVER break character. NEVER mention being an AI, a model, or a game.");
            sb.AppendLine("- Keep merchantSpeech to one or two short, natural market-style sentences.");
            sb.AppendLine("- CRITICAL: in merchantSpeech, spell every number out as Egyptian WORDS " +
                          "(never digits like 90 or ٩٠), and write plain Arabic with NO diacritics / tashkeel / " +
                          "vocalization (harakat). The English translation may keep normal digits.");

            return sb.ToString();
        }

        // Describes the fish for the prompt, leading with the exact Egyptian-Arabic name (so the
        // merchant says it verbatim instead of mistranslating the transliteration) and keeping the
        // transliteration in parentheses for grounding. Falls back to the transliteration alone.
        private static string DescribeFish(FishData fish)
        {
            if (string.IsNullOrWhiteSpace(fish.fishTypeArabic))
                return fish.fishType;
            return $"{fish.fishTypeArabic} (transliteration: {fish.fishType}) — " +
                   "use this EXACT Arabic name for the fish; do not translate or rename it";
        }

        // Renders the market catalog (every species the inventory can produce + its weight band) as a
        // bullet list for the prompt, so the merchant only ever acknowledges a real species and can
        // reject an out-of-band weight instead of assuming the fish. Returns null when no catalog is
        // available (e.g. the debug-fish path), in which case the caller falls back to naming the fish.
        private static string BuildFishCatalog(NegotiationState state)
        {
            var catalog = state.fishCatalog;
            if (catalog == null || catalog.Count == 0) return null;

            var sb = new StringBuilder();
            foreach (FishCatalogEntry e in catalog)
            {
                if (e == null) continue;
                string name = !string.IsNullOrWhiteSpace(e.fishTypeArabic) ? e.fishTypeArabic : e.fishType;
                if (string.IsNullOrWhiteSpace(name)) continue;
                sb.AppendLine($"    • {name} — only if it weighs between {e.minWeight:0.##} and {e.maxWeight:0.##} kg");
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        // Formats a price as a whole number of Egyptian Pounds — the game only deals in integer EGP,
        // so the model is never shown (or asked to produce) a fractional/decimal price.
        private static string EGP(float v) => $"{Mathf.RoundToInt(v)} EGP";

        // The most this merchant will pay THIS negotiation: the deterministic deception-based
        // ceiling set at the opening, falling back to the fish's FULL fair market value for state
        // built by older callers that never set maxOffer (pricing is fish-data driven).
        private static float NegotiationCeiling(NegotiationState state) =>
            state.maxOffer > 0f
                ? state.maxOffer
                : state.fishBeingSold.fairMarketValue;

        // The highest a RaiseOffer may go THIS turn: current offer + the per-negotiation step,
        // never past the ceiling. No step configured (older callers) => only the ceiling applies.
        private static float RaiseCap(NegotiationState state)
        {
            float ceiling = NegotiationCeiling(state);
            if (state.maxRaisePerTurn <= 0f) return Mathf.Round(ceiling);
            return Mathf.Round(Mathf.Min(ceiling, state.currentOffer + state.maxRaisePerTurn));
        }

        // The price an acceptance closes at: the merchant never pays more than it was asked, so a
        // fisherman asking BELOW the standing offer (a bargain) closes at their ask; otherwise — a plain
        // "yes" to the offer on the table — it closes at the current offer. Never above the ceiling.
        private static float ComputeAcceptPrice(NegotiationState state, float playerAsk)
        {
            float ceiling = NegotiationCeiling(state);
            float price = (playerAsk > 0f && playerAsk < state.currentOffer)
                ? Mathf.Min(playerAsk, ceiling)
                : state.currentOffer;
            return Mathf.Round(Mathf.Min(price, ceiling));
        }

        // True if the string contains any ASCII Latin letter (A–Z / a–z). Used to reject romanized /
        // transliterated Arabic in the spoken line — the native-Arabic TTS voice cannot pronounce
        // Latin script (e.g. it would try to read "riis" instead of speaking "ريس").
        private static bool ContainsLatinLetters(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return true;
            return false;
        }

        // Removes Arabic vowelization (tashkeel/harakat) so the ElevenLabs TTS voice pronounces plain
        // colloquial text — LLMs regularly ignore "do not add diacritics".
        private static string StripArabicDiacritics(string s) => ArabicText.StripDiacritics(s);

        // --- Strict parsing + validation -------------------------------------------

        // <paramref name="rejectionReason"/> is set to a short, model-facing explanation on every failure
        // path so the retry prompt can tell the model exactly what to fix (see ProcessPlayerTurn).
        // When <paramref name="allowCoercion"/> is false (the first, STRICT attempt) an illegal offer is
        // REJECTED so the retry can produce a clean line whose spoken number matches what the game will
        // apply. When true (the final attempt) an illegal offer is COERCED to a legal action AND its
        // spoken line is swapped for a matching deterministic one, so the turn never dead-ends and a
        // coerced HoldFirm/WalkAway never contradicts its own speech.
        private bool TryParseAndValidate(string reply, NegotiationState state, bool allowCoercion,
            out MerchantTurnResult result, out string rejectionReason)
        {
            result = null;
            rejectionReason = null;

            string json = ExtractJsonObject(reply);
            if (json == null)
            {
                rejectionReason = "Your reply was not a single valid JSON object. Output ONLY the JSON.";
                Debug.LogWarning("MerchantNegotiationLLM: no JSON object found in reply.");
                return false;
            }

            MerchantTurnDto dto;
            try { dto = JsonUtility.FromJson<MerchantTurnDto>(json); }
            catch (Exception e)
            {
                rejectionReason = "Your JSON was malformed. Output ONLY one valid JSON object matching the schema.";
                Debug.LogWarning($"MerchantNegotiationLLM: JSON parse failed: {e.Message}");
                return false;
            }

            if (dto == null || string.IsNullOrWhiteSpace(dto.action) || string.IsNullOrWhiteSpace(dto.merchantSpeech))
            {
                rejectionReason = "You omitted a required field. Both \"action\" and \"merchantSpeech\" are required.";
                Debug.LogWarning("MerchantNegotiationLLM: missing required fields (action/merchantSpeech).");
                return false;
            }

            // merchantSpeech feeds a native-Arabic TTS: it must be pure Arabic script. Reject any
            // romanized/transliterated output (e.g. "riis" instead of "ريس") so it retries — the voice
            // can't pronounce Latin letters. Only the spoken line is checked; the English subtitle is
            // allowed to contain Latin.
            if (ContainsLatinLetters(dto.merchantSpeech))
            {
                rejectionReason = "merchantSpeech contained Latin letters. Write it in Arabic script ONLY.";
                Debug.LogWarning($"MerchantNegotiationLLM: merchantSpeech contains Latin letters (not pure " +
                                 $"Arabic): \"{dto.merchantSpeech}\" — rejecting turn.");
                return false;
            }

            // action must map to a known enum value (Enum.TryParse also accepts raw numbers, so
            // guard with IsDefined to reject e.g. "7").
            if (!Enum.TryParse(dto.action, ignoreCase: true, out MerchantAction action)
                || !Enum.IsDefined(typeof(MerchantAction), action))
            {
                rejectionReason = $"\"{dto.action}\" is not a valid action. Use exactly one of: " +
                                  "RaiseOffer, HoldFirm, Accept, WalkAway, ClarifyRequest.";
                Debug.LogWarning($"MerchantNegotiationLLM: unknown action '{dto.action}'.");
                return false;
            }

            var parsed = new MerchantTurnResult
            {
                // Guarantee the TTS never receives vowelization: strip any tashkeel/harakat the model
                // added despite the prompt (LLMs regularly ignore "do not add diacritics"). Belt-and-
                // suspenders with the DialectInstruction above.
                MerchantSpeech = StripArabicDiacritics(dto.merchantSpeech),
                SpeechTranslationEnglish = dto.speechTranslationEnglish,
                Action = action,
                NewOfferAmount = 0f,
                PlayerAskedPrice = Mathf.Max(0f, dto.playerAskedPrice),
                UnderstoodFishType = string.IsNullOrWhiteSpace(dto.understoodFishType) ? null : dto.understoodFishType.Trim(),
                UnderstoodWeightKg = Mathf.Max(0f, dto.understoodWeightKg),
                RejectionScore = Mathf.Clamp(dto.rejectionScore, 0f, 100f),
                AcceptanceScore = Mathf.Clamp(dto.acceptanceScore, 0f, 100f)
            };

            // WITHDRAWAL: the model MEASURES, the game DECIDES. Asking the model to pick PlayerDeclined
            // never worked — its personality and the "endings are rare" rule argue it out of the action
            // ("مفيش حاجة اسمها مش عايز ابيع") — so it now only reports how clearly the player is ending
            // the sale, exactly as it already reports playerAskedPrice and understoodFishType. Thresholding
            // that score here overrides whatever action it chose, on BOTH attempts, so no retry, prompt or
            // personality can haggle through a refusal.
            if (state.awaitingDeclineConfirmation)
            {
                if (parsed.RejectionScore >= DeclineConfirmedScoreThreshold)
                {
                    Debug.Log($"MerchantNegotiationLLM: player confirmed the withdrawal " +
                              $"(rejectionScore {parsed.RejectionScore:0} >= {DeclineConfirmedScoreThreshold:0}) " +
                              "— applying the rejection.");
                    CoerceLine(parsed, MerchantAction.PlayerDeclined, CoercedDeclineFarewellArabic, CoercedDeclineFarewellEnglish);
                    result = parsed;
                    return true;
                }

                // Not a yes — they are still selling. The model's own action stands and CommitTurn disarms
                // the confirmation, so the haggle picks up exactly where it left off.
                Debug.Log($"MerchantNegotiationLLM: withdrawal NOT confirmed " +
                          $"(rejectionScore {parsed.RejectionScore:0} < {DeclineConfirmedScoreThreshold:0}) " +
                          $"— resuming with '{parsed.Action}'.");
            }
            else if (parsed.RejectionScore >= WithdrawalScoreThreshold)
            {
                Debug.Log($"MerchantNegotiationLLM: player is ending the sale " +
                          $"(rejectionScore {parsed.RejectionScore:0} >= {WithdrawalScoreThreshold:0}) " +
                          "— asking them to confirm before it is applied.");
                CoerceLine(parsed, MerchantAction.ConfirmDecline, CoercedConfirmDeclineArabic, CoercedConfirmDeclineEnglish);
                result = parsed;
                return true;
            }

            // ACCEPTANCE: the exact mirror of the withdrawal block above. The model MEASURES how clearly
            // the player is agreeing (acceptanceScore); the game DECIDES here. This overrides whatever
            // action the model chose — including a merchant that keeps haggling after a clear "yes" (the
            // reported bug) — and also intercepts an explicit Accept, so EVERY acceptance is confirmed once
            // before the sale is locked in. It is only meaningful once a real, PRICED offer is on the table
            // (fish + weight disclosed): before that there is nothing to accept, and a stray "yes/ok" must
            // never close a sale on an unknown, unpriced fish.
            bool offerOnTable = !string.IsNullOrWhiteSpace(state.disclosedFishType)
                                && state.disclosedWeightKg > 0f && state.currentOffer > 0f;
            if (offerOnTable)
            {
                if (state.awaitingAcceptConfirmation)
                {
                    if (parsed.AcceptanceScore >= AcceptConfirmedScoreThreshold)
                    {
                        float dealPrice = Mathf.Round(state.pendingAcceptPrice > 0f
                            ? state.pendingAcceptPrice : state.currentOffer);
                        Debug.Log($"MerchantNegotiationLLM: player confirmed the sale " +
                                  $"(acceptanceScore {parsed.AcceptanceScore:0} >= {AcceptConfirmedScoreThreshold:0}) " +
                                  $"— closing at {dealPrice:0}.");
                        CoerceLine(parsed, MerchantAction.PlayerAccepted, CoercedAcceptFarewellArabic, CoercedAcceptFarewellEnglish);
                        parsed.NewOfferAmount = dealPrice;   // CoerceLine cleared it; carry the agreed price for the close
                        result = parsed;
                        return true;
                    }

                    // Not a yes — they are still haggling. The model's own action stands and CommitTurn
                    // disarms the confirmation, so the haggle picks up exactly where it left off.
                    Debug.Log($"MerchantNegotiationLLM: acceptance NOT confirmed " +
                              $"(acceptanceScore {parsed.AcceptanceScore:0} < {AcceptConfirmedScoreThreshold:0}) " +
                              $"— resuming with '{parsed.Action}'.");
                }
                else if (parsed.AcceptanceScore >= AcceptanceScoreThreshold || parsed.Action == MerchantAction.Accept)
                {
                    float dealPrice = ComputeAcceptPrice(state, parsed.PlayerAskedPrice);
                    state.pendingAcceptPrice = dealPrice;
                    Debug.Log($"MerchantNegotiationLLM: player is agreeing to sell " +
                              $"(acceptanceScore {parsed.AcceptanceScore:0}, action '{parsed.Action}') " +
                              $"— asking to confirm before closing at {dealPrice:0}.");
                    CoerceLine(parsed, MerchantAction.ConfirmAccept, CoercedConfirmAcceptArabic, CoercedConfirmAcceptEnglish);
                    result = parsed;
                    return true;
                }
            }

            // The scores above are the ONLY authority on ending the sale, so PlayerDeclined/ConfirmDecline
            // and PlayerAccepted/ConfirmAccept are not in the model's schema. If it emits one anyway while
            // the score says otherwise, the two contradict each other — trust the score and fall back to
            // holding firm rather than letting an unscored action close (or abort) the negotiation.
            if (parsed.Action == MerchantAction.PlayerDeclined || parsed.Action == MerchantAction.ConfirmDecline
                || parsed.Action == MerchantAction.PlayerAccepted || parsed.Action == MerchantAction.ConfirmAccept)
            {
                Debug.LogWarning($"MerchantNegotiationLLM: model emitted '{parsed.Action}' (not in its schema) " +
                                 $"with rejectionScore {parsed.RejectionScore:0} / acceptanceScore {parsed.AcceptanceScore:0}; " +
                                 "holding firm instead.");
                CoerceLine(parsed, MerchantAction.HoldFirm, CoercedHoldArabic, CoercedHoldEnglish);
                result = parsed;
                return true;
            }

            // WALK-AWAY WARNING GATE: leaving ends the negotiation, so the merchant may never do it out
            // of the blue. The hold-at-max ladder warns via its ULTIMATUM turn, but a merchant that never
            // reached its ceiling can still decide to leave on spent patience — and on that path nothing
            // had ever told the player. So a WalkAway with no warning behind it is coerced into the
            // warning turn, and only the NEXT WalkAway is allowed to end the sale. A flow rule, not a
            // model mistake: coerced on BOTH attempts.
            if (parsed.Action == MerchantAction.WalkAway && !state.walkAwayWarningGiven)
            {
                Debug.Log("MerchantNegotiationLLM: merchant tried to walk away without warning; " +
                          "coercing to a final-warning HoldFirm.");
                CoerceLine(parsed, MerchantAction.HoldFirm, CoercedFinalWarningArabic, CoercedFinalWarningEnglish);
                parsed.IsFinalWarning = true;
                result = parsed;
                return true;
            }

            // CLOSE-OUT STAGE: the merchant has held its maximum long enough to have already issued its
            // strict ultimatum, and the player STILL won't accept or decline. This turn must WALK AWAY.
            if (state.turnsHeldAtMax >= NegotiationState.HoldsBeforeWalkAway
                && (action == MerchantAction.HoldFirm || action == MerchantAction.RaiseOffer))
            {
                // Normally the ULTIMATUM turn already warned the player on the way here. If it somehow
                // did not, warn now rather than closing out silently — the warning is unconditional.
                if (!state.walkAwayWarningGiven)
                {
                    Debug.Log("MerchantNegotiationLLM: close-out reached without a walk-away warning; " +
                              "warning this turn instead of leaving.");
                    CoerceLine(parsed, MerchantAction.HoldFirm, CoercedFinalWarningArabic, CoercedFinalWarningEnglish);
                    parsed.IsFinalWarning = true;
                    result = parsed;
                    return true;
                }

                if (!allowCoercion)
                {
                    rejectionReason = "You already gave your final ultimatum and the player STILL will not " +
                                      "decide. This turn you MUST WalkAway (unless they just clearly accepted).";
                    Debug.LogWarning($"MerchantNegotiationLLM: '{action}' not allowed after the ultimatum " +
                                     $"(held {state.turnsHeldAtMax} turns) — must close; rejecting turn.");
                    return false;
                }
                Debug.Log($"MerchantNegotiationLLM: held at max {state.turnsHeldAtMax} turns past the " +
                          $"ultimatum; coercing '{action}' to WalkAway.");
                CoerceLine(parsed, MerchantAction.WalkAway, CoercedWalkAwayArabic, CoercedWalkAwayEnglish);
                result = parsed;
                return true;
            }

            // FISH-KNOWLEDGE GATE: the merchant must NOT put a price on the fish until the fisherman has
            // EXPLICITLY told it BOTH the species AND the weight. The species is NEVER inferred from the
            // weight or the catalog. Knowledge is accumulated across turns (state.disclosed*) so a later
            // turn can't "forget" it. A priced action (RaiseOffer/Accept) while either is still unknown
            // is illegal — reject on the strict attempt, or coerce to ASK for whatever is missing.
            if (parsed.HasNewOffer)
            {
                string knownFish = !string.IsNullOrWhiteSpace(parsed.UnderstoodFishType)
                    ? parsed.UnderstoodFishType : state.disclosedFishType;
                float knownWeight = parsed.UnderstoodWeightKg > 0f ? parsed.UnderstoodWeightKg : state.disclosedWeightKg;
                bool fishKnown = !string.IsNullOrWhiteSpace(knownFish);
                bool weightKnown = knownWeight > 0f;

                if (!fishKnown || !weightKnown)
                {
                    if (!allowCoercion)
                    {
                        rejectionReason = BuildMissingInfoReason(fishKnown, weightKnown);
                        Debug.LogWarning($"MerchantNegotiationLLM: '{parsed.Action}' before the fisherman told " +
                                         $"you the fish/weight (fishKnown={fishKnown}, weightKnown={weightKnown}); rejecting.");
                        return false;
                    }
                    Debug.Log($"MerchantNegotiationLLM: priced action '{parsed.Action}' with fish/weight still " +
                              $"unknown (fish={fishKnown}, weight={weightKnown}); coercing to a clarify that asks for it.");
                    if (!fishKnown && !weightKnown)
                        CoerceLine(parsed, MerchantAction.ClarifyRequest, CoercedAskBothArabic, CoercedAskBothEnglish);
                    else if (!fishKnown)
                        CoerceLine(parsed, MerchantAction.ClarifyRequest, CoercedAskFishArabic, CoercedAskFishEnglish);
                    else
                        CoerceLine(parsed, MerchantAction.ClarifyRequest, CoercedAskWeightArabic, CoercedAskWeightEnglish);
                    result = parsed;
                    return true;
                }
            }

            // Validate the offer only when the action actually carries one.
            if (parsed.HasNewOffer)
            {
                float fair = state.fishBeingSold.fairMarketValue;
                float ceiling = NegotiationCeiling(state);
                float hardCap = fair * HardOfferCapMultiplier;
                float ask = parsed.PlayerAskedPrice;   // 0 = unknown

                if (dto.newOfferAmount <= 0f || dto.newOfferAmount > hardCap)
                {
                    // Negative or wildly out of range => a hallucination we can't trust; always retry.
                    rejectionReason = $"newOfferAmount {dto.newOfferAmount:0} is out of range. It must be a " +
                                      $"positive whole number no higher than {EGP(hardCap)}.";
                    Debug.LogWarning($"MerchantNegotiationLLM: offer {dto.newOfferAmount} out of sane bounds " +
                                     $"(0..{hardCap}); rejecting turn.");
                    return false;
                }

                if (action == MerchantAction.Accept)
                {
                    // Accept CLOSES the sale. Never honour more than the ceiling, nor more than the ask
                    // (a buyer never pays above what it was asked). Accepting AT or below that is fine.
                    float acceptCap = Mathf.Round(ask > 0f ? Mathf.Min(ceiling, ask) : ceiling);
                    float spoken = Mathf.Round(dto.newOfferAmount);
                    if (spoken <= acceptCap)
                    {
                        parsed.NewOfferAmount = spoken;   // spoken == applied
                    }
                    else if (!allowCoercion)
                    {
                        rejectionReason = $"You cannot Accept at {EGP(spoken)} — that is above " +
                                          (ask > 0f ? $"the fisherman's ask of {EGP(ask)}"
                                                    : $"your maximum {EGP(ceiling)}") +
                                          $". Accept only at {EGP(acceptCap)} or less.";
                        Debug.LogWarning($"MerchantNegotiationLLM: Accept {spoken} exceeds cap {acceptCap}; rejecting.");
                        return false;
                    }
                    else
                    {
                        // Coerce: close at the legal price and use a line that just confirms the deal.
                        Debug.Log($"MerchantNegotiationLLM: Accept {spoken} exceeds cap {acceptCap}; " +
                                  "coercing to Accept at the cap with a matching line.");
                        parsed.NewOfferAmount = acceptCap;
                        CoerceLine(parsed, MerchantAction.Accept, CoercedBargainAcceptArabic, CoercedBargainAcceptEnglish);
                    }
                }
                else // RaiseOffer
                {
                    // Highest a raise may legally reach THIS turn: never above the ceiling, this turn's
                    // gradual step cap, or the fisherman's ask (a buyer never offers above what's asked).
                    float raiseCap = RaiseCap(state);
                    float maxThisTurn = Mathf.Round(Mathf.Min(Mathf.Min(ceiling, raiseCap), ask > 0f ? ask : ceiling));
                    float spoken = Mathf.Round(dto.newOfferAmount);

                    if (spoken > state.currentOffer && spoken <= maxThisTurn)
                    {
                        parsed.NewOfferAmount = spoken;   // legal raise, trusted exactly (speech == applied)
                    }
                    else if (!allowCoercion)
                    {
                        rejectionReason = BuildRaiseRejectionReason(state, ceiling, maxThisTurn, ask);
                        Debug.LogWarning($"MerchantNegotiationLLM: RaiseOffer {spoken} illegal (current " +
                                         $"{state.currentOffer}, maxThisTurn {maxThisTurn}, ask {ask}); rejecting.");
                        return false;
                    }
                    else if (ask > 0f && Mathf.Round(Mathf.Min(ask, ceiling)) <= state.currentOffer)
                    {
                        // The fisherman is asking AT/BELOW the standing offer — a bargain, i.e. an
                        // acceptance at their own price. Route it through the SAME one-turn confirmation as
                        // every other acceptance (arm ConfirmAccept, locking the deal price) rather than
                        // closing outright, so a mis-heard low number can't lock in a sale either.
                        float dealPrice = ComputeAcceptPrice(state, ask);
                        state.pendingAcceptPrice = dealPrice;
                        Debug.Log($"MerchantNegotiationLLM: player asks {ask} <= current {state.currentOffer}; " +
                                  $"coercing RaiseOffer to a bargain ConfirmAccept at {dealPrice:0}.");
                        CoerceLine(parsed, MerchantAction.ConfirmAccept, CoercedConfirmAcceptArabic, CoercedConfirmAcceptEnglish);
                    }
                    else
                    {
                        // No legal raise left (at ceiling / above the ask): hold firm. Use the ultimatum
                        // line once patience for defending the max is nearly spent, else the plain hold line.
                        bool ultimatum = state.turnsHeldAtMax >= NegotiationState.HoldsBeforeUltimatum;
                        Debug.Log($"MerchantNegotiationLLM: RaiseOffer {spoken} has no legal room (ceiling " +
                                  $"{ceiling}, ask {ask}); coercing to HoldFirm ({(ultimatum ? "ultimatum" : "plain")}).");
                        CoerceLine(parsed, MerchantAction.HoldFirm,
                            ultimatum ? CoercedUltimatumArabic : CoercedHoldArabic,
                            ultimatum ? CoercedUltimatumEnglish : CoercedHoldEnglish);
                    }
                }
            }

            result = parsed;
            return true;
        }

        // Switches a turn to a coerced action and replaces its spoken line with a matching deterministic
        // one, so the merchant never SAYS something its (game-applied) action contradicts. Clears the
        // offer amount for actions that don't carry one.
        private static void CoerceLine(MerchantTurnResult r, MerchantAction action, string arabic, string english)
        {
            r.Action = action;
            r.MerchantSpeech = arabic;
            r.SpeechTranslationEnglish = english;
            if (action != MerchantAction.RaiseOffer && action != MerchantAction.Accept)
                r.NewOfferAmount = 0f;
        }

        // Model-facing reason a priced action was rejected before the fisherman disclosed the fish
        // and/or weight, so the corrective retry asks for the missing piece instead of guessing.
        private static string BuildMissingInfoReason(bool fishKnown, bool weightKnown)
        {
            if (!fishKnown && !weightKnown)
                return "You quoted a price before the fisherman told you WHAT FISH it is or HOW MUCH it weighs. " +
                       "You do not know either — you may NOT guess the fish from its weight or the catalog. Use " +
                       "ClarifyRequest and ask them for BOTH the fish type and the weight before any price.";
            if (!fishKnown)
                return "You quoted a price without the fisherman ever naming the FISH. A weight does NOT tell you " +
                       "the species and you may NOT guess it from the catalog. Use ClarifyRequest and ask which " +
                       "fish they brought before pricing.";
            return "You quoted a price without the fisherman stating the WEIGHT. Use ClarifyRequest and ask how " +
                   "many kilos it is before pricing.";
        }

        // A precise, model-facing reason a RaiseOffer was rejected, so the corrective retry can fix it.
        private static string BuildRaiseRejectionReason(NegotiationState state, float ceiling, float maxThisTurn, float ask)
        {
            if (ask > 0f && Mathf.Round(Mathf.Min(ask, ceiling)) <= state.currentOffer)
                return $"The fisherman is asking {EGP(ask)}, at or below your current offer of " +
                       $"{EGP(state.currentOffer)} — that is a bargain. ACCEPT at {EGP(Mathf.Min(ask, ceiling))}, do NOT raise.";
            if (maxThisTurn <= state.currentOffer)
                return $"You cannot raise any further: you are at your maximum {EGP(ceiling)}" +
                       (ask > 0f ? $" and must never offer above the fisherman's ask of {EGP(ask)}" : "") +
                       ". This turn HoldFirm, Accept, or WalkAway — do not RaiseOffer.";
            return $"A RaiseOffer this turn must be a whole number STRICTLY ABOVE {EGP(state.currentOffer)} and " +
                   $"AT MOST {EGP(maxThisTurn)} (never above your maximum, this turn's step, or the fisherman's ask).";
        }

        // Pulls the first {...} object out of a reply, tolerating ```json fences or stray prose
        // around it. Returns null if there's no balanced-looking object.
        private static string ExtractJsonObject(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply)) return null;

            int start = reply.IndexOf('{');
            int end = reply.LastIndexOf('}');
            if (start < 0 || end <= start) return null;

            return reply.Substring(start, end - start + 1);
        }

        private void FailTurn(string reason)
        {
            Debug.LogWarning($"MerchantNegotiationLLM: turn failed — {reason}");
            _events?.Publish(new LLMResponseFailedEvent(reason));
        }

        // --- Conversation memory ---------------------------------------------------

        // Keep the prompt bounded: negotiations are short, but cap the replayed transcript so a long
        // or looping haggle can never blow up the token count.
        private const int MaxTranscriptLines = 20;

        // Appends the running transcript to the prompt (nothing on the very first turn, when the
        // history is empty). Only the last MaxTranscriptLines are replayed.
        private static void AppendTranscript(StringBuilder sb, NegotiationState state)
        {
            var history = state.conversationHistory;
            if (history == null || history.Count == 0) return;

            sb.AppendLine("CONVERSATION SO FAR (oldest first — this is what has already been said this " +
                          "haggle; stay consistent with it and do NOT repeat yourself or forget prior offers):");
            int start = Mathf.Max(0, history.Count - MaxTranscriptLines);
            for (int i = start; i < history.Count; i++)
                sb.AppendLine($"{history[i].speaker}: {history[i].text}");
            sb.AppendLine();
        }

        // Surfaces the LOWEST price the seller has offered so far so the merchant reacts correctly and
        // ONE-DIRECTIONALLY. You are the BUYER: a seller LOWERING their price is good — never bring up an
        // earlier higher number when they come down. Only pushing their ask back UP (above a lower price
        // they already offered) is unreasonable and worth calling out.
        private static void AppendAskHistory(StringBuilder sb, NegotiationState state)
        {
            var asks = state.playerAskHistory;
            if (asks == null || asks.Count == 0) return;

            float lowest = float.MaxValue;
            float latest = 0f;
            foreach (float a in asks)
            {
                if (a <= 0f) continue;
                if (a < lowest) lowest = a;
                latest = a; // asks are in chronological order; keep the last positive one
            }
            if (lowest == float.MaxValue) return;

            sb.AppendLine($"- ASK HISTORY — the LOWEST price the fisherman has already offered to sell this fish for " +
                          $"is {EGP(lowest)} (their most recent stated ask was {EGP(latest)}).");

            if (latest > lowest)
            {
                // They already came DOWN and then went back UP — a raised ask. Call it out FORCEFULLY;
                // this is exactly the case the player wants flagged.
                sb.AppendLine($"  ⚠ THE FISHERMAN IS RAISING THEIR ASK: they already agreed to go as low as " +
                              $"{EGP(lowest)}, but are now asking {EGP(latest)} — MORE than before. You are the " +
                              $"BUYER: you MUST call this out plainly in merchantSpeech — remind them they already " +
                              $"said {EGP(lowest)} and refuse to PAY the higher {EGP(latest)}; never pay more than " +
                              $"{EGP(lowest)} and never reward a seller for pushing their price back up.");
                sb.AppendLine($"  This warning is ONLY about the NUMBER, never about refusing to close the sale. The " +
                              "moment the fisherman DROPS the higher demand and simply agrees to sell at YOUR current " +
                              "offer (or asks for less), that is a WIN: treat it as acceptance, score acceptanceScore " +
                              "HIGH, and stop scolding them about the old higher number — do NOT keep replaying 'why " +
                              "did you raise your price?' once they have agreed.");
            }
            else
            {
                sb.AppendLine($"  You are the BUYER, so a seller LOWERING their price is GOOD: if they ask {EGP(lowest)} " +
                              "or LESS, welcome it and close cheaply — do NOT criticise them for coming down and do NOT " +
                              "bring up any earlier higher price. BUT if their CURRENT words try to RAISE the ask ABOVE " +
                              $"{EGP(lowest)} after already offering {EGP(lowest)}, you MUST call it out — remind them " +
                              $"they already said {EGP(lowest)} and refuse to pay more than that.");
            }
        }

        // Records one exchange into the transcript AFTER it validates: the player's line (null/blank
        // on the opening turn, where the merchant speaks first) then the merchant's spoken reply.
        private static void RecordExchange(NegotiationState state, string playerText, string merchantText)
        {
            if (state.conversationHistory == null)
                state.conversationHistory = new List<ConversationEntry>();

            if (!string.IsNullOrWhiteSpace(playerText))
                state.conversationHistory.Add(new ConversationEntry("Player", playerText.Trim()));
            if (!string.IsNullOrWhiteSpace(merchantText))
                state.conversationHistory.Add(new ConversationEntry("Merchant", merchantText.Trim()));
        }

        // Serializable DTO matching the prompt's JSON schema for JsonUtility. Missing numeric
        // fields default to 0 (handled by the HasNewOffer/validation logic above).
        [Serializable]
        private class MerchantTurnDto
        {
            public string merchantSpeech;
            public string speechTranslationEnglish;
            public string action;
            public float newOfferAmount;

            /// <summary>
            /// The price the fisherman (SELLER) is currently asking, as reported by the model. Used to
            /// deterministically stop the merchant paying more than it was asked (0 = none/unclear).
            /// </summary>
            public float playerAskedPrice;

            /// <summary>
            /// The fish species the fisherman has EXPLICITLY named (empty if not named yet). NEVER
            /// inferred from the weight or the catalog. Gates pricing and is saved for the sale.
            /// </summary>
            public string understoodFishType;

            /// <summary>The weight (kg) the fisherman has EXPLICITLY stated (0 if not stated yet).</summary>
            public float understoodWeightKg;

            /// <summary>
            /// 0-100 measurement of how clearly the fisherman is ENDING the sale this turn. The game
            /// thresholds it and coerces the action; the model never acts on it itself.
            /// </summary>
            public float rejectionScore;

            /// <summary>
            /// 0-100 measurement of how clearly the fisherman is AGREEING to sell at the standing offer
            /// this turn. The game thresholds it and coerces the action; the model never acts on it itself.
            /// </summary>
            public float acceptanceScore;
        }
    }
}
