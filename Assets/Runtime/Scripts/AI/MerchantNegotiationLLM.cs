using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The dialogue action the LLM declares for a turn. Overlaps with the deterministic
    /// <see cref="NegotiationOutcome"/> (Accept/RaiseOffer/HoldFirm/WalkAway) but adds
    /// <see cref="ClarifyRequest"/> for when the player's speech was unclear. NOTE: deception
    /// is NOT in here — whether the merchant lowballs is decided deterministically by
    /// <see cref="PriceNegotiationCalculator"/> and passed in; the LLM only colours the tone.
    /// </summary>
    public enum MerchantAction
    {
        RaiseOffer,
        HoldFirm,
        Accept,
        WalkAway,
        ClarifyRequest
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

        // The Egyptian-Arabic constraint from docs/MERCHANT_CONTEXT.md. It MUST appear in EVERY
        // prompt (LLMs drift toward MSA by default), so it lives here and is injected each turn.
        private const string DialectInstruction =
            "Respond only in Egyptian Arabic dialect (اللهجة المصرية العامية), not Modern Standard " +
            "Arabic. Use natural, casual market speech, not formal or written Arabic.";

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
                return result;

            // Attempt 2: simplified prompt — strips flavour, re-asserts the JSON-only contract.
            Debug.LogWarning("MerchantNegotiationLLM: first attempt invalid; retrying with simplified prompt.");
            reply = await SendPromptAsync(BuildPrompt(state, playerSpeechText, isDeceptive, simplified: true));
            if (TryParseAndValidate(reply, state, out result))
                return result;

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
        public async Task<MerchantTurnResult> GenerateOpeningLine(NegotiationState state, bool isDeceptive)
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

            string reply = await SendPromptAsync(BuildOpeningPrompt(state, isDeceptive, simplified: false));
            if (TryParseAndValidate(reply, state, out MerchantTurnResult result))
                return result;

            Debug.LogWarning("MerchantNegotiationLLM: opening line invalid; retrying with simplified prompt.");
            reply = await SendPromptAsync(BuildOpeningPrompt(state, isDeceptive, simplified: true));
            if (TryParseAndValidate(reply, state, out result))
                return result;

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
            return LLMManager.Singleton.GenerateTextAsync(prompt);
        }

        // --- Prompt construction ---------------------------------------------------

        private string BuildPrompt(NegotiationState state, string playerSpeechText, bool isDeceptive, bool simplified)
        {
            MerchantConfig merchant = state.currentMerchant;
            FishData fish = state.fishBeingSold;
            int patienceRemaining = Mathf.Max(0, merchant.PatienceLevel - state.turnCount);
            string playerSaid = string.IsNullOrWhiteSpace(playerSpeechText) ? "(unclear / no speech)" : playerSpeechText;

            var sb = new StringBuilder();

            sb.AppendLine("You are an NPC fish merchant at the El Borg fish auction market in Dumyat (Damietta), Egypt.");
            sb.AppendLine(DialectInstruction);
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

            sb.AppendLine("NEGOTIATION STATE:");
            sb.AppendLine($"- Fish type: {fish.fishType}");
            sb.AppendLine($"- Fish weight (kg): {fish.weight}");
            sb.AppendLine($"- Your current offer: {state.currentOffer}");
            sb.AppendLine($"- Fair market value (do NOT reveal this number to the player): {fish.fairMarketValue}");
            sb.AppendLine($"- Turn number: {state.turnCount}");
            sb.AppendLine($"- Your patience remaining (counter-offers left before you walk away): {patienceRemaining}");
            sb.AppendLine($"- Player just said (transcribed Egyptian Arabic speech): \"{playerSaid}\"");
            sb.AppendLine();

            sb.AppendLine("RESPOND WITH EXACTLY ONE JSON OBJECT AND NOTHING ELSE. Schema:");
            sb.AppendLine("{");
            sb.AppendLine("  \"merchantSpeech\": \"<what you say out loud, in Egyptian Arabic dialect>\",");
            sb.AppendLine("  \"speechTranslationEnglish\": \"<English translation of merchantSpeech, for subtitles>\",");
            sb.AppendLine("  \"action\": \"RaiseOffer\" | \"HoldFirm\" | \"Accept\" | \"WalkAway\" | \"ClarifyRequest\",");
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
            sb.AppendLine("- Keep merchantSpeech to one or two short, natural market-style sentences.");

            return sb.ToString();
        }

        // Opening-line prompt: greet + announce the FIXED opening offer. Same JSON schema so the
        // existing parser/validator is reused; action is pinned to HoldFirm (a no-op the caller
        // ignores) and the model is told the price is fixed so it can't move the number.
        private string BuildOpeningPrompt(NegotiationState state, bool isDeceptive, bool simplified)
        {
            MerchantConfig merchant = state.currentMerchant;
            FishData fish = state.fishBeingSold;

            var sb = new StringBuilder();

            sb.AppendLine("You are an NPC fish merchant at the El Borg fish auction market in Dumyat (Damietta), Egypt.");
            sb.AppendLine(DialectInstruction);
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

            sb.AppendLine("THIS IS YOUR OPENING LINE. The player just walked up to sell you their fish.");
            sb.AppendLine($"- Fish type: {fish.fishType}");
            sb.AppendLine($"- Fish weight (kg): {fish.weight}");
            sb.AppendLine($"- Your opening price (FIXED — say exactly this number, do not change it): {state.currentOffer}");
            sb.AppendLine("Greet the player briefly and state your opening price for the fish.");
            sb.AppendLine();

            sb.AppendLine("RESPOND WITH EXACTLY ONE JSON OBJECT AND NOTHING ELSE. Schema:");
            sb.AppendLine("{");
            sb.AppendLine("  \"merchantSpeech\": \"<your greeting + opening price, in Egyptian Arabic dialect>\",");
            sb.AppendLine("  \"speechTranslationEnglish\": \"<English translation, for subtitles>\",");
            sb.AppendLine("  \"action\": \"HoldFirm\",");
            sb.AppendLine("  \"newOfferAmount\": 0");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine("HARD RULES:");
            sb.AppendLine("- Output ONLY the JSON object. No markdown, no code fences, no text before or after it.");
            sb.AppendLine("- NEVER break character. NEVER mention being an AI, a model, or a game.");
            sb.AppendLine("- Keep merchantSpeech to one or two short, natural market-style sentences.");

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

            // action must map to a known enum value (Enum.TryParse also accepts raw numbers, so
            // guard with IsDefined to reject e.g. "7").
            if (!Enum.TryParse(dto.action, ignoreCase: true, out MerchantAction action)
                || !Enum.IsDefined(typeof(MerchantAction), action))
            {
                Debug.LogWarning($"MerchantNegotiationLLM: unknown action '{dto.action}'.");
                return false;
            }

            var parsed = new MerchantTurnResult
            {
                MerchantSpeech = dto.merchantSpeech,
                SpeechTranslationEnglish = dto.speechTranslationEnglish,
                Action = action,
                NewOfferAmount = 0f
            };

            // Validate/clamp the offer only when the action actually carries one.
            if (parsed.HasNewOffer)
            {
                float fair = state.fishBeingSold.fairMarketValue;
                float ceiling = fair * state.currentMerchant.MaxOfferMultiplier;
                float hardCap = fair * HardOfferCapMultiplier;

                if (dto.newOfferAmount <= 0f || dto.newOfferAmount > hardCap)
                {
                    // Negative or wildly out of range => don't trust the model; treat as failure.
                    Debug.LogWarning($"MerchantNegotiationLLM: offer {dto.newOfferAmount} out of sane bounds " +
                                     $"(0..{hardCap}); rejecting turn.");
                    return false;
                }

                // In-range but above what this merchant would ever pay: clamp to the real ceiling
                // so the game logic is never asked to honour a too-generous promise.
                parsed.NewOfferAmount = Mathf.Min(dto.newOfferAmount, ceiling);
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
