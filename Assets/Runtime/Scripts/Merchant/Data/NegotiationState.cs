using System.Collections.Generic;

namespace DumyatRuntime
{
    /// <summary>
    /// The phase a single fish negotiation is currently in.
    /// </summary>
    public enum NegotiationPhase
    {
        /// <summary>Player is walking up to the merchant; no offers yet.</summary>
        Approaching,

        /// <summary>Active back-and-forth haggling over the price.</summary>
        Negotiating,

        /// <summary>Player accepted an offer; the sale completed.</summary>
        Accepted,

        /// <summary>Player declined and ended the negotiation themselves.</summary>
        Declined,

        /// <summary>Merchant ran out of patience and walked away.</summary>
        WalkedAway
    }

    /// <summary>
    /// One line of the spoken back-and-forth in a negotiation, kept so the LLM can be reminded of
    /// the full conversation each turn (it is stateless between requests). <see cref="text"/> is the
    /// Egyptian-Arabic line as spoken; <see cref="speaker"/> is "Merchant" or "Player".
    /// </summary>
    public class ConversationEntry
    {
        public string speaker;
        public string text;

        public ConversationEntry(string speaker, string text)
        {
            this.speaker = speaker;
            this.text = text;
        }
    }

    /// <summary>
    /// Mutable, in-flight state of one active fish negotiation between the player
    /// and a single merchant. Plain C# class (not a MonoBehaviour) — created and
    /// owned by the negotiation logic for the duration of a single sale.
    /// </summary>
    public class NegotiationState
    {
        /// <summary>The merchant the player is currently haggling with.</summary>
        public MerchantConfig currentMerchant;

        /// <summary>The fish item being sold in this negotiation.</summary>
        public FishData fishBeingSold;

        /// <summary>The merchant's current standing offer, in game currency.</summary>
        public float currentOffer;

        /// <summary>
        /// Ordered history of every offer made this negotiation (oldest first).
        /// Used for prompt context and to detect stalled haggling.
        /// </summary>
        public List<float> offerHistory = new List<float>();

        /// <summary>
        /// Ordered transcript of the spoken exchange so far (oldest first): the merchant's opening
        /// line, then alternating player/merchant lines. Replayed into every LLM prompt so the
        /// merchant "remembers" what the player already said and what was already offered.
        /// </summary>
        public List<ConversationEntry> conversationHistory = new List<ConversationEntry>();

        /// <summary>Number of negotiation turns taken so far this sale.</summary>
        public int turnCount;

        /// <summary>Current phase of the negotiation.</summary>
        public NegotiationPhase state = NegotiationPhase.Approaching;
    }
}
