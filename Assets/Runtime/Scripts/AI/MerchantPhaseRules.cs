using System.Collections.Generic;
using System.Text;

namespace DumyatRuntime
{
    /// <summary>
    /// The distinct conversational PHASES the merchant's LLM layer speaks or classifies in. Each phase
    /// owns its OWN rule block in <see cref="MerchantPhaseRules"/>, so guidance written for one phase can
    /// never bleed into another — the class of bug where the sale-confirmation criteria were used to judge
    /// a <em>placement</em> answer ("دي كل اللي معايا" / "this is all I have" read as a price refusal
    /// instead of "that's my whole catch"). The same negotiation turn loop runs throughout; the phase
    /// selects which rules govern the turn.
    /// </summary>
    public enum MerchantPhase
    {
        /// <summary>Greet and ask the fisherman to load the scale — nothing is priced yet.</summary>
        Welcome,

        /// <summary>"Is that your whole catch?" — before pricing, confirm everything is on the scale.</summary>
        PlacementConfirm,

        /// <summary>The opening line: acknowledge the catch (or ask what it is) and state the fixed offer.</summary>
        Opening,

        /// <summary>The price back-and-forth over the catch.</summary>
        Haggle,

        /// <summary>Read the per-fish prices + total back and ask the player to confirm the sale.</summary>
        SaleSummary,

        /// <summary>Classify the player's yes/no answer to CLOSING the sale.</summary>
        SaleConfirm,

        /// <summary>The closing line spoken once the summary is confirmed and the deal is sealed.</summary>
        SaleClosed,

        /// <summary>A playful reaction to a fumbled fish.</summary>
        Fumble
    }

    /// <summary>
    /// The single home for the merchant LLM's per-phase RULES. Every phase's situation framing and
    /// behavioural directives live together here, keyed by <see cref="MerchantPhase"/>, so they can be
    /// read and edited in one place and one phase's rules can never leak into another. Prompt builders in
    /// <see cref="MerchantNegotiationLLM"/> compose these blocks with the cross-cutting pieces they own
    /// (the live TTS dialect instruction, the vendor personality, and — for <see cref="MerchantPhase.Haggle"/>
    /// / <see cref="MerchantPhase.Opening"/> — the per-turn pricing numbers, which are runtime PARAMETERS,
    /// not reusable rules, so they stay in the builder).
    /// </summary>
    internal static class MerchantPhaseRules
    {
        /// <summary>The market-role line that opens EVERY merchant prompt.</summary>
        public const string MarketRole =
            "You are an NPC fish MERCHANT (a buyer) at the El Borg fish auction market in Dumyat (Damietta), Egypt.";

        public static void AppendRole(StringBuilder sb) => sb.AppendLine(MarketRole);

        /// <summary>
        /// The "the player is a FISHERMAN selling to you" framing shared by the <see cref="MerchantPhase.Opening"/>
        /// and <see cref="MerchantPhase.Haggle"/> prompts. Bundle mode sells the WHOLE catch on the scale as one
        /// lot; single mode haggles one fish. The two phases word the bundle line slightly differently (haggle
        /// stresses the ongoing deal; opening just states the lot), so the phase selects the exact wording.
        /// </summary>
        public static void AppendSellingFraming(StringBuilder sb, MerchantPhase phase, bool bundleMode)
        {
            if (bundleMode)
            {
                if (phase == MerchantPhase.Opening)
                {
                    sb.AppendLine("The player is a FISHERMAN who has just loaded their fresh catch onto YOUR SCALE to SELL it " +
                                  "to you — YOU are buying it from them.");
                    sb.AppendLine("You buy the WHOLE catch on your scale as ONE lot for ONE total price.");
                }
                else
                {
                    sb.AppendLine("The player is a FISHERMAN who has put their fresh catch on YOUR SCALE to SELL it to you — " +
                                  "YOU are buying it from them.");
                    sb.AppendLine("You buy and haggle over the WHOLE catch on your scale (all the fish listed below) as ONE deal " +
                                  "for ONE total price. Nothing else is for sale and the player cannot buy anything from you. " +
                                  "If they say anything off-topic, steer them back to selling this catch (use ClarifyRequest).");
                }
            }
            else
            {
                sb.AppendLine("The player is a FISHERMAN who just caught a fish and walked up to SELL it to you — YOU are buying it from them.");
                sb.AppendLine("You ONLY buy and haggle over THIS one fish. Nothing else is for sale and the player cannot buy anything from you. " +
                              "If they say anything off-topic, steer them back to selling this fish (use ClarifyRequest).");
            }
        }

        // --- Welcome ---------------------------------------------------------------

        /// <summary>The situation framing for the scale-flow WELCOME line (nothing placed yet).</summary>
        public static void AppendWelcomeSituation(StringBuilder sb)
        {
            sb.AppendLine("A FISHERMAN has walked up to sell their catch. You have a SCALE where they place the fish they want to sell.");
        }

        /// <summary>What the merchant must SAY in the welcome line (ask the fisherman to load the scale).</summary>
        public static void AppendWelcomeDirective(StringBuilder sb, bool withGreeting)
        {
            sb.AppendLine("THIS IS YOUR WELCOME LINE. Nothing is on your scale yet — you cannot see or price any fish.");
            sb.AppendLine(withGreeting
                ? "Greet the fisherman warmly (Egyptian market style: 'يا ريس' / 'اهلا يا معلم' / 'يا باشا'), then tell " +
                  "them to put the fish they want to sell ON YOUR SCALE and talk to you again when they are done. Short and friendly."
                : "You already know this fisherman — skip the greeting. Just tell them to put the fish they want to sell " +
                  "ON YOUR SCALE and talk to you again when they are done. Short and natural.");
            sb.AppendLine("Do NOT name any fish, weight, or price — nothing has been placed yet.");
        }

        // --- Placement confirm (spoken question) -----------------------------------

        /// <summary>The situation framing for the "is that your whole catch?" spoken question.</summary>
        public static void AppendPlacementConfirmSituation(StringBuilder sb)
        {
            sb.AppendLine("A FISHERMAN has placed some fish on your SCALE. Before you price the catch you want to be SURE he " +
                          "has loaded EVERYTHING he wants to sell — he may still have more.");
        }

        /// <summary>What the merchant must SAY to ask whether the whole catch is placed (a yes/no question).</summary>
        public static void AppendPlacementConfirmDirective(StringBuilder sb)
        {
            sb.AppendLine("Ask the fisherman — warmly and briefly — whether that is his WHOLE catch, or if he still has more " +
                          "fish to put on the scale. Make it a clear yes/no question (a 'yes' means that's everything and you " +
                          "will price it now). Do NOT name any fish, weight, or price yet.");
        }

        // --- Sale summary (spoken read-back) ---------------------------------------

        /// <summary>
        /// The situation framing for the SALE SUMMARY read-back, naming any fish the player just added or
        /// removed so the merchant acknowledges the change before reading the updated deal.
        /// </summary>
        public static void AppendSummarySituation(StringBuilder sb, IReadOnlyList<string> addedFish,
            IReadOnlyList<string> removedFish)
        {
            sb.AppendLine("You have agreed a running TOTAL price with a FISHERMAN for the catch resting on your scale, " +
                          "and you are reviewing the deal with them.");

            bool anyRemoved = removedFish != null && removedFish.Count > 0;
            bool anyAdded = addedFish != null && addedFish.Count > 0;
            if (anyRemoved)
                sb.AppendLine("NOTE: the fisherman JUST TOOK these fish BACK OFF the scale: " +
                              string.Join("، ", removedFish) + ". The deal is now smaller by that much.");
            if (anyAdded)
                sb.AppendLine("NOTE: the fisherman JUST ADDED these fish to the scale: " +
                              string.Join("، ", addedFish) + ". The deal is now bigger by their value.");
            if (anyAdded || anyRemoved)
                sb.AppendLine("Briefly acknowledge BY NAME what just changed (what they added / took off), then read the " +
                              "UPDATED deal and total below.");
        }

        // --- Sale closed (spoken close, after the summary is confirmed) -------------

        /// <summary>The situation framing for the CLOSING line — the deal is now truly agreed.</summary>
        public static void AppendSaleClosedSituation(StringBuilder sb)
        {
            sb.AppendLine("A FISHERMAN has just CONFIRMED the price summary for the whole catch on your scale. The deal is " +
                          "now DONE — you have agreed to buy the lot at the total below.");
        }

        /// <summary>What the merchant must SAY to seal the agreed sale (warm close, ends the conversation).</summary>
        public static void AppendSaleClosedDirective(StringBuilder sb)
        {
            sb.AppendLine("Warmly SEAL the deal: tell the fisherman it's agreed and the sale is done, and give a friendly " +
                          "Egyptian blessing (e.g. 'الله يبارك لك في رزقك'). You MAY restate the ONE total you are paying " +
                          "(spell it out as Egyptian words). Keep it to one or two short, warm sentences. This ENDS the " +
                          "conversation — do NOT ask any further question.");
        }

        // --- Fumble ----------------------------------------------------------------

        /// <summary>The situation framing for a fumbled fish (hit the merchant, or missed the scale).</summary>
        public static void AppendFumbleSituation(StringBuilder sb, FishFumbleReason reason)
        {
            sb.AppendLine("A FISHERMAN is trying to place fish on your scale to sell them, but just fumbled:");
            sb.AppendLine(reason == FishFumbleReason.HitMerchant
                ? "- They threw or dropped a fish and it HIT YOU."
                : "- They dropped a fish on the GROUND next to the scale instead of ON it.");
        }

        /// <summary>What the merchant must SAY when the player fumbles a fish (a playful quip).</summary>
        public static void AppendFumbleDirective(StringBuilder sb)
        {
            sb.AppendLine("React with ONE short, GOOD-NATURED, funny Egyptian market quip — tease them lightly, never truly " +
                          "angry — then remind them to put the fish GENTLY on the scale. Keep it playful and brief. Do NOT " +
                          "name a price.");
        }

        // --- Confirmation classifiers (yes/no answers) -----------------------------
        //
        // The bleed-prone pair. A confirmation is a yes/no classification, and the SAME words mean opposite
        // things depending on which question was asked, so the criteria are split by phase and chosen here —
        // never shared. PlacementConfirm judges "have you placed everything?"; SaleConfirm judges "do we have
        // a deal?".

        /// <summary>
        /// Builds the full yes/no classification prompt for the given confirmation <paramref name="phase"/>
        /// (<see cref="MerchantPhase.PlacementConfirm"/> or <see cref="MerchantPhase.SaleConfirm"/>).
        /// </summary>
        public static string BuildConfirmationPrompt(MerchantPhase phase, string playerSpeechText)
        {
            var sb = new StringBuilder();
            if (phase == MerchantPhase.PlacementConfirm)
            {
                sb.AppendLine("A fisherman at a fish market put some fish on the merchant's scale and was asked whether " +
                              "that is his WHOLE catch — i.e. has he placed EVERYTHING he wants to sell (a yes/no " +
                              "question). Decide, based ONLY on what he said, whether he is DONE placing fish.");
                sb.AppendLine($"What the fisherman said (Egyptian Arabic, transcribed): \"{playerSpeechText}\"");
                sb.AppendLine("Reply as exactly one JSON object and nothing else:");
                sb.AppendLine("{\"answer\": \"yes\" | \"no\" | \"unclear\"}");
                sb.AppendLine("- \"yes\"  = that is everything / he is done placing (اه، ايوه، خلاص، دي كل اللي معايا، " +
                              "كله هنا، مفيش غيرهم، دول بس، اه دول كلهم).");
                sb.AppendLine("- \"no\"   = he has more to add or is not done yet (لا، لسه، فيه كمان، استنى هحط تاني، هزود).");
                sb.AppendLine("- \"unclear\" = anything ambiguous, unrelated, or empty.");
                sb.AppendLine("IMPORTANT: this is NOT about price or agreeing to a deal. \"دي كل اللي معايا\" / \"this is " +
                              "all I have\" means YES (that is the whole catch), NOT a refusal.");
            }
            else
            {
                sb.AppendLine("A fisherman at a fish market was asked to CONFIRM a sale (a yes/no question). Decide whether " +
                              "they are AGREEING to finalize the sale, based ONLY on what they said.");
                sb.AppendLine($"What the fisherman said (Egyptian Arabic, transcribed): \"{playerSpeechText}\"");
                sb.AppendLine("Reply as exactly one JSON object and nothing else:");
                sb.AppendLine("{\"answer\": \"yes\" | \"no\" | \"unclear\"}");
                sb.AppendLine("- \"yes\"  = they agree / confirm the sale (اه، ايوه، اكيد، اتفقنا، ماشي، تمام، خلاص بيعتهالك).");
                sb.AppendLine("- \"no\"   = they decline, back out, or want to change something (لا، مش عايز، استنى، عايز اغير).");
                sb.AppendLine("- \"unclear\" = anything ambiguous, unrelated, or empty.");
            }
            sb.AppendLine("Output ONLY the JSON object.");
            return sb.ToString();
        }
    }
}
