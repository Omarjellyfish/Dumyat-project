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

        /// <summary>
        /// The fish species the fisherman has EXPLICITLY named so far (empty until they actually say
        /// it). Accumulated from the LLM's per-turn report and never inferred from weight/catalog, so
        /// the merchant can be hard-blocked from pricing a fish it was never told. Also the value saved
        /// for the fish-sell process / inventory validation.
        /// </summary>
        public string disclosedFishType;

        /// <summary>
        /// The weight (kg) the fisherman has EXPLICITLY stated so far (0 until they actually say it).
        /// Paired with <see cref="disclosedFishType"/> to gate pricing and to save the deal's fish data.
        /// </summary>
        public float disclosedWeightKg;

        /// <summary>
        /// The market catalog (every species the inventory can produce, with weight bands) so the
        /// merchant can ask what the fisherman brought, only acknowledge real species, and reject
        /// out-of-band weights — instead of assuming the fish. May be null/empty if no catalog exists.
        /// </summary>
        public List<FishCatalogEntry> fishCatalog;

        /// <summary>The merchant's current standing offer, in game currency.</summary>
        public float currentOffer;

        /// <summary>
        /// The ABSOLUTE most this merchant will pay THIS negotiation, decided deterministically at
        /// the opening (see <see cref="PriceNegotiationCalculator.GenerateMaxOffer"/>) from the same
        /// deception roll as the opening offer. Offers only ever move UP from
        /// <see cref="currentOffer"/> toward this ceiling and never beyond it.
        /// </summary>
        public float maxOffer;

        /// <summary>
        /// The deception roll made at the opening of THIS negotiation. Reused for every turn so the
        /// merchant's tone stays consistent with the (deception-based) opening offer and
        /// <see cref="maxOffer"/> envelope, instead of flipping per turn.
        /// </summary>
        public bool isDeceptive;

        /// <summary>
        /// The most a single RaiseOffer may add on top of <see cref="currentOffer"/>, decided at
        /// the opening (a fraction of the opening→ceiling gap, see
        /// <see cref="PriceNegotiationCalculator.GenerateMaxRaiseStep"/>). Guarantees the merchant
        /// concedes GRADUALLY over several turns instead of jumping from its lowball straight to
        /// <see cref="maxOffer"/> in one reply.
        /// </summary>
        public float maxRaisePerTurn;

        /// <summary>
        /// Ordered history of every offer made this negotiation (oldest first).
        /// Used for prompt context and to detect stalled haggling.
        /// </summary>
        public List<float> offerHistory = new List<float>();

        /// <summary>
        /// Ordered history of every price the PLAYER (seller) has asked for this negotiation (oldest
        /// first), as reported by the LLM each turn. Lets the merchant notice when the fisherman
        /// RAISES their asking price mid-haggle (e.g. 150 then 190) and push back / hold firmer.
        /// </summary>
        public List<float> playerAskHistory = new List<float>();

        /// <summary>
        /// Ordered transcript of the spoken exchange so far (oldest first): the merchant's opening
        /// line, then alternating player/merchant lines. Replayed into every LLM prompt so the
        /// merchant "remembers" what the player already said and what was already offered.
        /// </summary>
        public List<ConversationEntry> conversationHistory = new List<ConversationEntry>();

        /// <summary>Number of negotiation turns taken so far this sale.</summary>
        public int turnCount;

        /// <summary>
        /// How many turns the merchant has HELD FIRM while its offer already sat at
        /// <see cref="maxOffer"/>. Drives a two-stage close-out so the player is never cut off
        /// abruptly: while below <see cref="HoldsBeforeUltimatum"/> the merchant just keeps holding
        /// firm (the player may keep trying); at <see cref="HoldsBeforeUltimatum"/> it issues a STRICT
        /// ultimatum ("this is final — accept or decline NOW"); only once it reaches
        /// <see cref="HoldsBeforeWalkAway"/> (the player still won't decide) does it walk away.
        /// </summary>
        public int turnsHeldAtMax;

        /// <summary>
        /// Held-at-max turns before the merchant stops just holding and issues its STRICT
        /// accept-or-decline ULTIMATUM (still not walking away — the player gets this turn to decide).
        /// </summary>
        public const int HoldsBeforeUltimatum = 2;

        /// <summary>
        /// Held-at-max turns after which the merchant finally WALKS AWAY — i.e. the player kept
        /// haggling even after the ultimatum instead of accepting or declining.
        /// </summary>
        public const int HoldsBeforeWalkAway = 3;

        /// <summary>
        /// True for the ONE turn that follows the merchant asking the player to confirm they really want
        /// to call the sale off (see <see cref="MerchantAction.ConfirmDecline"/>). It switches how the
        /// model's rejection score is read — "are they withdrawing?" becomes "did they answer yes?" — and
        /// a decline is only ever APPLIED while it is set, so the player can never lose a sale to a single
        /// mis-heard sentence. On the confirming turn the rejection takes effect immediately. Cleared
        /// again as soon as the player says anything that resumes the haggle.
        ///
        /// The merchant cannot ask twice in a row: <see cref="MerchantAction.ConfirmDecline"/> is only
        /// produced while this is clear, and producing it sets it.
        /// </summary>
        public bool awaitingDeclineConfirmation;

        /// <summary>
        /// True for the ONE turn that follows the merchant asking the player to confirm they really want
        /// to sell at the standing offer (see <see cref="MerchantAction.ConfirmAccept"/>). The exact
        /// mirror of <see cref="awaitingDeclineConfirmation"/>: it switches how the model's acceptance
        /// score is read — "are they agreeing?" becomes "did they answer yes?" — and an acceptance is
        /// only ever APPLIED while it is set, so the player can never lose a sale (or lock in a price) to
        /// a single mis-heard sentence. Cleared again as soon as the player says anything that resumes
        /// the haggle. The merchant cannot ask twice in a row: <see cref="MerchantAction.ConfirmAccept"/>
        /// is only produced while this is clear, and producing it sets it.
        /// </summary>
        public bool awaitingAcceptConfirmation;

        /// <summary>
        /// The price the sale will close at once the player confirms an acceptance, captured when the
        /// merchant asks them to confirm (see <see cref="awaitingAcceptConfirmation"/>). Locking it here
        /// means the confirming turn closes at exactly the number the player agreed to — never a value
        /// that drifted in between. Only meaningful while <see cref="awaitingAcceptConfirmation"/> is set.
        /// </summary>
        public float pendingAcceptPrice;

        /// <summary>
        /// True once the merchant has spent a turn TELLING the player it is about to leave. A
        /// <see cref="MerchantAction.WalkAway"/> is never applied while this is false — it is coerced
        /// into that warning turn instead — so the player is never dropped mid-haggle without notice.
        /// Set by the hold-at-max ULTIMATUM turn (see <see cref="HoldsBeforeUltimatum"/>) or by the
        /// coerced warning when the merchant tries to leave on exhausted patience.
        /// </summary>
        public bool walkAwayWarningGiven;

        /// <summary>Current phase of the negotiation.</summary>
        public NegotiationPhase state = NegotiationPhase.Approaching;
    }
}
