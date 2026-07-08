using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
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
        /// The PLAYER clearly refused to sell or ended the conversation; the merchant speaks a
        /// short good-natured farewell and the negotiation resolves as
        /// <see cref="NegotiationPhase.Declined"/>.
        /// </summary>
        PlayerDeclined
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
    public class MerchantNegotiationLLM : MonoBehaviour, IManager
    {
        public static MerchantNegotiationLLM Singleton { get; private set; }

        // An offer this far above fair value is treated as the model hallucinating, not haggling.
        private const float HardOfferCapMultiplier = 2f;

        // Fallback description of the speech engine for the prompt when the TTS pool can't be
        // queried (manager missing / nothing available). Kept generic but truthful.
        private const string FallbackTtsDescription = "a native Egyptian-Arabic ElevenLabs text-to-speech voice";

        // The Egyptian-Arabic constraint from docs/MERCHANT_CONTEXT.md. It MUST appear in EVERY
        // prompt (LLMs drift toward MSA by default), so it lives here and is injected each turn.
        // Built per call (not a const) so the prompt names the EXACT TTS engine that will speak
        // the reply — resolved live from TTSManager (e.g. "ElevenLabs TTS (eleven_multilingual_v2)").
        private static string BuildDialectInstruction()
        {
            string ttsName = TTSManager.Singleton != null ? TTSManager.Singleton.ActiveModelName : null;
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
        /// Fired when a turn cannot produce a valid result after the retry. The argument is a
        /// human-readable reason. The negotiation system should respond with a generic scripted
        /// line (e.g. "I didn't quite catch that, can you repeat?").
        /// </summary>
        public event Action<string> OnLLMResponseFailed;

        private void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }
            Singleton = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Runs one negotiation turn through the LLM and returns a validated
        /// <see cref="MerchantTurnResult"/>, or null if it could not be produced (in which case
        /// <see cref="OnLLMResponseFailed"/> has already fired). Tries a full prompt first, then a
        /// simplified prompt once, before giving up.
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

            if (LLMManager.Singleton == null || !LLMManager.Singleton.HasAvailableModel)
            {
                FailTurn("No LLM model available.");
                return null;
            }

            // Attempt 1: full prompt.
            string reply = await SendPromptAsync(BuildPrompt(state, playerSpeechText, isDeceptive, simplified: false));
            if (TryParseAndValidate(reply, state, out MerchantTurnResult result))
            {
                RecordExchange(state, playerSpeechText, result.MerchantSpeech);
                return result;
            }

            // Attempt 2: simplified prompt — strips flavour, re-asserts the JSON-only contract.
            Debug.LogWarning("MerchantNegotiationLLM: first attempt invalid; retrying with simplified prompt.");
            reply = await SendPromptAsync(BuildPrompt(state, playerSpeechText, isDeceptive, simplified: true));
            if (TryParseAndValidate(reply, state, out result))
            {
                RecordExchange(state, playerSpeechText, result.MerchantSpeech);
                return result;
            }

            FailTurn("LLM returned no valid structured response after retry.");
            return null;
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

            if (LLMManager.Singleton == null || !LLMManager.Singleton.HasAvailableModel)
            {
                FailTurn("No LLM model available (opening line).");
                return null;
            }

            string reply = await SendPromptAsync(BuildOpeningPrompt(state, isDeceptive, withGreeting, simplified: false));
            if (TryParseAndValidate(reply, state, out MerchantTurnResult result))
            {
                RecordExchange(state, null, result.MerchantSpeech); // opening: merchant speaks first
                return result;
            }

            Debug.LogWarning("MerchantNegotiationLLM: opening line invalid; retrying with simplified prompt.");
            reply = await SendPromptAsync(BuildOpeningPrompt(state, isDeceptive, withGreeting, simplified: true));
            if (TryParseAndValidate(reply, state, out result))
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
            if (LLMManager.Singleton == null) return Task.FromResult<string>(null);
            // jsonObject: force the provider into structured-output mode (Groq json_object / Gemini
            // application/json) so the reply is a single valid JSON object, not prose we then fail to
            // parse and recover from.
            return LLMManager.Singleton.GenerateTextAsync(prompt, jsonObject: true);
        }

        // --- Prompt construction ---------------------------------------------------

        private string BuildPrompt(NegotiationState state, string playerSpeechText, bool isDeceptive, bool simplified)
        {
            MerchantConfig merchant = state.currentMerchant;
            FishData fish = state.fishBeingSold;
            int patienceRemaining = Mathf.Max(0, merchant.PatienceLevel - state.turnCount);
            string playerSaid = string.IsNullOrWhiteSpace(playerSpeechText) ? "(unclear / no speech)" : playerSpeechText;

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

            // Deception is decided by game logic, not the model. We only tell it the TONE to take.
            sb.AppendLine(isDeceptive
                ? "TONE THIS TURN: you are quietly trying to get a bargain (lowball) — be warm and persuasive, " +
                  "not openly dishonest or villainous. Never admit you are lowballing."
                : "TONE THIS TURN: you are dealing fairly and openly this turn.");
            sb.AppendLine();

            // Replay the whole conversation so the (stateless) model stays consistent with what it
            // and the player have already said and offered, instead of treating each turn as new.
            AppendTranscript(sb, state);

            sb.AppendLine("WHAT YOU KNOW:");
            sb.AppendLine("- You do NOT automatically know what fish this is or how much it weighs. You only " +
                          "learn that from what the fisherman tells you in the conversation above. If they " +
                          "have NOT yet told you what they caught and roughly how much it weighs, ASK them " +
                          "for it (ClarifyRequest) before quoting any price — never invent a fish or a weight " +
                          "they did not say.");
            sb.AppendLine($"- ONLY once the fisherman has told you the fish, refer to it by its correct " +
                          $"Egyptian name: {DescribeFish(fish)}");
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
            sb.AppendLine();

            float raiseCap = RaiseCap(state);

            sb.AppendLine("HOW YOU HAGGLE (offers move in ONE direction only — UP, and only a little at a time):");
            sb.AppendLine("- You are the BUYER: you start low and can be argued UPWARD toward your maximum. You " +
                          "NEVER lower an offer you have already made — a RaiseOffer's newOfferAmount MUST be " +
                          $"HIGHER than your current {EGP(state.currentOffer)} and NEVER above your maximum {EGP(ceiling)}.");
            sb.AppendLine($"- Concede GRADUALLY: if you raise THIS TURN, newOfferAmount must be between " +
                          $"{EGP(state.currentOffer + 1f)} and {EGP(raiseCap)}. Never jump straight to your " +
                          "maximum — small, believable market steps, and you do NOT have to reach your maximum.");
            sb.AppendLine("- If the player has only just told you what fish they have and no price talk has " +
                          $"happened yet, QUOTE your current offer {EGP(state.currentOffer)} in your speech " +
                          "(action HoldFirm) — the haggle is just beginning.");
            if (state.turnsHeldAtMax >= NegotiationState.MaxTurnsHeldAtMax)
                sb.AppendLine($"- FINAL TURN — YOUR PATIENCE FOR DEFENDING YOUR MAXIMUM IS SPENT: you already " +
                              $"refused to go above your maximum {state.turnsHeldAtMax} times and the player is " +
                              "still pushing. Unless they clearly accept RIGHT NOW, choose WalkAway with ONE " +
                              "short, firm but polite rejection line that ENDS the deal (my final word, no deal, " +
                              "good luck elsewhere). Do NOT HoldFirm again. Do NOT RaiseOffer.");
            else if (atCeiling)
                sb.AppendLine("- YOU ARE AT YOUR MAXIMUM RIGHT NOW: do NOT use RaiseOffer again this negotiation. Either " +
                              "HoldFirm (insist this is your final price), Accept, or WalkAway if the player keeps pushing.");
            else
                sb.AppendLine("- If the player demands MORE than your maximum, do not exceed it: HoldFirm near your " +
                              "ceiling (make clear you're near your limit) or WalkAway when your patience runs out.");
            sb.AppendLine("- Accept when the player's ask is at/below your current offer, or when a deal at or " +
                          "below your maximum feels won. \"newOfferAmount\" for Accept is the agreed price.");
            sb.AppendLine();

            sb.AppendLine("CLOSING THE DEAL — Accept and PlayerDeclined END the sale, so be VERY strict:");
            sb.AppendLine($"- Accept ONLY when the player EXPLICITLY agrees to sell at your CURRENT offer with " +
                          $"clear deal words ('اتفقنا', 'خلاص موافق', 'ماشي خدها') and NO new number attached. " +
                          $"Then newOfferAmount = {EGP(state.currentOffer)}. If they mention ANY other price or " +
                          "condition, that is haggling — NOT acceptance.");
            sb.AppendLine("- PlayerDeclined ONLY when the player clearly refuses to SELL AT ALL or ends the " +
                          "conversation ('مش هبيع خلاص', 'مع السلامة', 'هدور على غيرك'). Saying no to YOUR " +
                          "PRICE ('لا ده قليل', 'مش كفاية', 'بتهزر؟') is NOT declining — that is normal " +
                          "haggling: answer with HoldFirm or a gradual RaiseOffer.");
            sb.AppendLine("- DEFAULT WHEN UNSURE: keep negotiating (HoldFirm / RaiseOffer / ClarifyRequest). " +
                          "Ending the sale by mistake ruins the game round; treat Accept and PlayerDeclined as " +
                          "rare, definitive endings you only pick on unmistakable words.");
            sb.AppendLine();

            sb.AppendLine("Return your reply as exactly one valid json object and nothing else. Schema:");
            sb.AppendLine("{");
            sb.AppendLine("  \"merchantSpeech\": \"<what you say out loud, in Egyptian dialect>\",");
            sb.AppendLine("  \"speechTranslationEnglish\": \"<English translation of merchantSpeech, for subtitles>\",");
            sb.AppendLine("  \"action\": \"RaiseOffer\" | \"HoldFirm\" | \"Accept\" | \"WalkAway\" | \"ClarifyRequest\" | \"PlayerDeclined\",");
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
            sb.AppendLine($"- RaiseOffer: newOfferAmount MUST be STRICTLY HIGHER than {EGP(state.currentOffer)} " +
                          $"and AT MOST {EGP(raiseCap)} this turn. Never lower a standing offer; never exceed " +
                          "your maximum; never jump more than this turn's cap.");
            sb.AppendLine("- Keep merchantSpeech to one or two short, natural market-style sentences.");
            sb.AppendLine("- Every price is in Egyptian Pounds (بالجنيه المصري) and MUST be a WHOLE number — " +
                          "never decimals, fractions or piastres (say 'تسعين جنيه', never 'تسعين جنيه ونص'). " +
                          "\"newOfferAmount\" must be a whole integer.");
            sb.AppendLine("- CRITICAL: in merchantSpeech, spell every number out as Egyptian WORDS " +
                          "(never digits like 90 or ٩٠), and write plain Arabic with NO diacritics / tashkeel / " +
                          "vocalization (harakat). The English translation may keep normal digits.");

            return sb.ToString();
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
                  "'اهلا يا معلم'), then ask the fisherman what they've got for you today and roughly how " +
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

        // Formats a price as a whole number of Egyptian Pounds — the game only deals in integer EGP,
        // so the model is never shown (or asked to produce) a fractional/decimal price.
        private static string EGP(float v) => $"{Mathf.RoundToInt(v)} EGP";

        // The most this merchant will pay THIS negotiation: the deterministic deception-based
        // ceiling set at the opening, falling back to the archetype ceiling for state built by
        // older callers that never set maxOffer.
        private static float NegotiationCeiling(NegotiationState state) =>
            state.maxOffer > 0f
                ? state.maxOffer
                : state.fishBeingSold.fairMarketValue * state.currentMerchant.MaxOfferMultiplier;

        // The highest a RaiseOffer may go THIS turn: current offer + the per-negotiation step,
        // never past the ceiling. No step configured (older callers) => only the ceiling applies.
        private static float RaiseCap(NegotiationState state)
        {
            float ceiling = NegotiationCeiling(state);
            if (state.maxRaisePerTurn <= 0f) return Mathf.Round(ceiling);
            return Mathf.Round(Mathf.Min(ceiling, state.currentOffer + state.maxRaisePerTurn));
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
        // colloquial text. Strips the combining-mark ranges the model might emit: fatha/damma/kasra/
        // tanwin/shadda/sukun and the extended marks (U+064B–U+065F) plus the superscript alef
        // (U+0670). Base letters and spaces are untouched.
        private static string StripArabicDiacritics(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                // U+064B..U+065F (harakat/tanwin/shadda/sukun + extended marks) and U+0670 (superscript alef).
                if ((c >= 'ً' && c <= 'ٟ') || c == 'ٰ')
                    continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        // --- Strict parsing + validation -------------------------------------------

        private bool TryParseAndValidate(string reply, NegotiationState state, out MerchantTurnResult result)
        {
            result = null;

            string json = ExtractJsonObject(reply);
            if (json == null)
            {
                Debug.LogWarning("MerchantNegotiationLLM: no JSON object found in reply.");
                return false;
            }

            MerchantTurnDto dto;
            try { dto = JsonUtility.FromJson<MerchantTurnDto>(json); }
            catch (Exception e)
            {
                Debug.LogWarning($"MerchantNegotiationLLM: JSON parse failed: {e.Message}");
                return false;
            }

            if (dto == null || string.IsNullOrWhiteSpace(dto.action) || string.IsNullOrWhiteSpace(dto.merchantSpeech))
            {
                Debug.LogWarning("MerchantNegotiationLLM: missing required fields (action/merchantSpeech).");
                return false;
            }

            // merchantSpeech feeds a native-Arabic TTS: it must be pure Arabic script. Reject any
            // romanized/transliterated output (e.g. "riis" instead of "ريس") so it retries — the voice
            // can't pronounce Latin letters. Only the spoken line is checked; the English subtitle is
            // allowed to contain Latin.
            if (ContainsLatinLetters(dto.merchantSpeech))
            {
                Debug.LogWarning($"MerchantNegotiationLLM: merchantSpeech contains Latin letters (not pure " +
                                 $"Arabic): \"{dto.merchantSpeech}\" — rejecting turn.");
                return false;
            }

            // action must map to a known enum value (Enum.TryParse also accepts raw numbers, so
            // guard with IsDefined to reject e.g. "7").
            if (!Enum.TryParse(dto.action, ignoreCase: true, out MerchantAction action)
                || !Enum.IsDefined(typeof(MerchantAction), action))
            {
                Debug.LogWarning($"MerchantNegotiationLLM: unknown action '{dto.action}'.");
                return false;
            }

            // Once the merchant has exhausted its patience defending the ceiling, the turn MUST
            // close the negotiation one way or another (Accept / WalkAway / PlayerDeclined, with
            // ClarifyRequest still allowed for genuinely garbled speech). Holding firm yet again
            // is rejected so a stubborn player can't stall the haggle at the ceiling forever.
            if (state.turnsHeldAtMax >= NegotiationState.MaxTurnsHeldAtMax
                && (action == MerchantAction.HoldFirm || action == MerchantAction.RaiseOffer))
            {
                Debug.LogWarning($"MerchantNegotiationLLM: '{action}' not allowed after holding the max " +
                                 $"offer {state.turnsHeldAtMax} turns — merchant must close; rejecting turn.");
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
                NewOfferAmount = 0f
            };

            // Validate/clamp the offer only when the action actually carries one.
            if (parsed.HasNewOffer)
            {
                float fair = state.fishBeingSold.fairMarketValue;
                float ceiling = NegotiationCeiling(state);
                float hardCap = fair * HardOfferCapMultiplier;

                if (dto.newOfferAmount <= 0f || dto.newOfferAmount > hardCap)
                {
                    // Negative or wildly out of range => don't trust the model; treat as failure.
                    Debug.LogWarning($"MerchantNegotiationLLM: offer {dto.newOfferAmount} out of sane bounds " +
                                     $"(0..{hardCap}); rejecting turn.");
                    return false;
                }

                // In-range but above what this merchant will pay THIS negotiation: clamp to the
                // deterministic ceiling so the game never honours a too-generous promise. Prices are
                // whole Egyptian Pounds, so round to an integer.
                parsed.NewOfferAmount = Mathf.Round(Mathf.Min(dto.newOfferAmount, ceiling));

                // Offers are MONOTONIC: a merchant never walks an offer back down. A "raise" that
                // (after clamping) isn't strictly above the standing offer means the model tried to
                // lower the price or exceed its ceiling while already at it — reject so the retry
                // re-asserts the rules instead of letting the spoken number contradict game state.
                if (action == MerchantAction.RaiseOffer && parsed.NewOfferAmount <= state.currentOffer)
                {
                    Debug.LogWarning($"MerchantNegotiationLLM: RaiseOffer {dto.newOfferAmount} does not move " +
                                     $"above the current offer {state.currentOffer} (ceiling {ceiling}); rejecting turn.");
                    return false;
                }

                // Raises are also GRADUAL: a single concession may not cover more than the
                // per-negotiation step (a fraction of the opening→ceiling gap), so the merchant
                // climbs to its maximum over several turns instead of jumping there. Rejected (not
                // clamped) so the spoken number never contradicts the applied one.
                if (action == MerchantAction.RaiseOffer && parsed.NewOfferAmount > RaiseCap(state))
                {
                    Debug.LogWarning($"MerchantNegotiationLLM: RaiseOffer {dto.newOfferAmount} exceeds this " +
                                     $"turn's cap {RaiseCap(state)} (current {state.currentOffer} + step " +
                                     $"{state.maxRaisePerTurn}); rejecting turn.");
                    return false;
                }
            }

            result = parsed;
            return true;
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
            OnLLMResponseFailed?.Invoke(reason);
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
        }
    }
}
