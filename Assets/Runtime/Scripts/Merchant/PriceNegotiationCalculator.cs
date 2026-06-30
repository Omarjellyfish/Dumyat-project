using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The merchant's decision on a player's counter-offer. Pure intent — the caller
    /// (and later the LLM layer) turns this into dialogue and, for <see cref="RaiseOffer"/>,
    /// picks the new number.
    /// </summary>
    public enum NegotiationOutcome
    {
        /// <summary>Take the player's price; the sale closes.</summary>
        Accept,

        /// <summary>Willing to move up toward the player.</summary>
        RaiseOffer,

        /// <summary>Stand on the current offer without budging.</summary>
        HoldFirm,

        /// <summary>Out of patience — leave the negotiation.</summary>
        WalkAway
    }

    /// <summary>
    /// Pure, deterministic pricing + deception math for a fish negotiation. No Unity
    /// MonoBehaviour, no networking, no AI — the LLM layer wraps these numbers with
    /// personality and dialogue later. All tuning knobs live as named constants here
    /// so the feel can be adjusted through playtesting without touching the flow.
    ///
    /// Randomness uses <see cref="UnityEngine.Random"/> to match the rest of the
    /// codebase; call <c>Random.InitState(seed)</c> before exercising this in a test
    /// if you need reproducible rolls.
    /// </summary>
    public class PriceNegotiationCalculator
    {
        // --- Tuning constants (safe to tweak during playtesting) -------------------

        /// <summary>Upper bound on a DECEPTIVE opening offer multiplier (kept below 1.0).</summary>
        private const float DeceptiveOfferCeiling = 0.8f;

        /// <summary>Lower bound on an HONEST opening offer multiplier.</summary>
        private const float HonestOfferFloor = 0.85f;

        /// <summary>
        /// If the player's demand is within this fraction of the merchant's standing
        /// offer, the merchant just accepts rather than haggling further.
        /// </summary>
        private const float AcceptGapFraction = 0.05f;

        // Optional global guard-rails on the final offer. See note on _systemConfig.
        private readonly MerchantSystemConfig _systemConfig;

        /// <param name="systemConfig">
        /// Global config supplying the offer guard-rail multipliers
        /// (<see cref="MerchantSystemConfig.BasePriceRangeMin"/>/<c>Max</c>). May be null,
        /// in which case no clamp is applied. These should be set WIDE (e.g. 0.4–1.2) — they
        /// are a safety net against misconfigured merchants, NOT a tight price band, or they
        /// will clamp away intended lowballing.
        /// </param>
        public PriceNegotiationCalculator(MerchantSystemConfig systemConfig = null)
        {
            _systemConfig = systemConfig;
        }

        /// <summary>
        /// Rolls whether this merchant is being deceptive (lowballing) for a single offer,
        /// using its <see cref="MerchantConfig.DeceptionRate"/>. Exposed so the negotiation
        /// flow can drive BOTH the offer amount and the LLM's dialogue tone from the SAME roll
        /// (the LLM only expresses the deception; it never decides it).
        /// </summary>
        public bool RollDeception(MerchantConfig merchant)
        {
            return merchant != null && Random.value < merchant.DeceptionRate;
        }

        /// <summary>
        /// Produces the merchant's first offer for a fish, rolling deception internally.
        /// Use the overload taking an explicit <c>deceiving</c> flag when the caller needs the
        /// roll result (e.g. to colour LLM dialogue) so the offer and tone stay consistent.
        /// </summary>
        public float GenerateOpeningOffer(MerchantConfig merchant, FishData fish)
        {
            return GenerateOpeningOffer(merchant, fish, RollDeception(merchant));
        }

        /// <summary>
        /// Produces the merchant's first offer for a fish with a caller-supplied deception
        /// decision: a deceiving merchant lowballs (multiplier in
        /// [minOfferMultiplier, <see cref="DeceptiveOfferCeiling"/>]); an honest one offers
        /// fairly (multiplier in [<see cref="HonestOfferFloor"/>, maxOfferMultiplier]). The
        /// result is clamped to the global guard-rails when a system config is present.
        /// </summary>
        public float GenerateOpeningOffer(MerchantConfig merchant, FishData fish, bool deceiving)
        {
            if (merchant == null || fish == null)
            {
                Debug.LogWarning("PriceNegotiationCalculator: null merchant or fish in GenerateOpeningOffer.");
                return 0f;
            }

            float multiplier = deceiving
                ? RandomInRange(merchant.MinOfferMultiplier, DeceptiveOfferCeiling)
                : RandomInRange(HonestOfferFloor, merchant.MaxOfferMultiplier);

            float offer = fish.fairMarketValue * multiplier;
            return ClampToGuardRails(offer, fish.fairMarketValue);
        }

        /// <summary>
        /// Decides how the merchant reacts to the player's counter-offer. Reads
        /// <see cref="NegotiationState"/> but does NOT mutate it (the caller owns turn
        /// advancement and applying any new offer).
        ///
        /// Rules, in order:
        /// 1. Player asking for ≤ the standing offer → <see cref="NegotiationOutcome.Accept"/>.
        /// 2. Turns taken ≥ patience → <see cref="NegotiationOutcome.WalkAway"/>.
        /// 3. Demand above the merchant's ceiling (fair × maxOfferMultiplier) → walk away if
        ///    patience is nearly spent, else hold firm — so low-patience merchants bail faster
        ///    on repeated high demands.
        /// 4. Demand within reach: accept if it's within <see cref="AcceptGapFraction"/> of the
        ///    standing offer; otherwise raise — unless a deception roll makes the merchant hold
        ///    firm, so more deceptive merchants resist raising more often.
        /// </summary>
        public NegotiationOutcome EvaluateCounterOffer(NegotiationState state, float playerCounterOffer)
        {
            if (state == null || state.currentMerchant == null || state.fishBeingSold == null)
            {
                Debug.LogWarning("PriceNegotiationCalculator: incomplete NegotiationState in EvaluateCounterOffer.");
                return NegotiationOutcome.HoldFirm;
            }

            MerchantConfig merchant = state.currentMerchant;
            float standingOffer = state.currentOffer;
            float ceiling = state.fishBeingSold.fairMarketValue * merchant.MaxOfferMultiplier;

            // 1. Player wants no more than already offered — easy yes.
            if (playerCounterOffer <= standingOffer)
            {
                return NegotiationOutcome.Accept;
            }

            // 2. Patience exhausted.
            if (state.turnCount >= merchant.PatienceLevel)
            {
                return NegotiationOutcome.WalkAway;
            }

            // 3. Demand beyond what this merchant will ever pay.
            if (playerCounterOffer > ceiling)
            {
                // Bail when patience is nearly spent; lower patience reaches this sooner.
                if (state.turnCount >= merchant.PatienceLevel - 1)
                {
                    return NegotiationOutcome.WalkAway;
                }
                return NegotiationOutcome.HoldFirm;
            }

            // 4. Demand is within reach (standingOffer < demand <= ceiling).
            float acceptGap = standingOffer * AcceptGapFraction;
            if (playerCounterOffer - standingOffer <= acceptGap)
            {
                return NegotiationOutcome.Accept;
            }

            // Deceptive merchants resist raising: higher deceptionRate => more HoldFirm.
            if (Random.value < merchant.DeceptionRate)
            {
                return NegotiationOutcome.HoldFirm;
            }
            return NegotiationOutcome.RaiseOffer;
        }

        /// <summary>
        /// Scores how well the player did, 0–100: 100 means they got full fair value or
        /// better, lower means they were talked down further below it.
        /// </summary>
        public float CalculateSuccessRate(float finalAcceptedPrice, float fairMarketValue)
        {
            if (fairMarketValue <= 0f)
            {
                Debug.LogWarning("PriceNegotiationCalculator: non-positive fairMarketValue in CalculateSuccessRate.");
                return 0f;
            }

            float ratio = finalAcceptedPrice / fairMarketValue * 100f;
            return Mathf.Clamp(ratio, 0f, 100f);
        }

        // --- Helpers ---------------------------------------------------------------

        // Random.Range(float,float) does not guarantee behaviour when min > max, so order
        // the bounds first (a misconfigured merchant could invert them).
        private static float RandomInRange(float a, float b)
        {
            return a <= b ? Random.Range(a, b) : Random.Range(b, a);
        }

        private float ClampToGuardRails(float offer, float fairMarketValue)
        {
            if (_systemConfig == null) return offer;

            float min = fairMarketValue * _systemConfig.BasePriceRangeMin;
            float max = fairMarketValue * _systemConfig.BasePriceRangeMax;
            if (min > max) (min, max) = (max, min);
            return Mathf.Clamp(offer, min, max);
        }
    }
}

// ---------------------------------------------------------------------------------
// MANUAL VERIFICATION — example calls and expected behaviour.
//
// These are illustrative, not an automated test suite (Unity Test Framework is
// installed but no test assemblies exist yet). Drop these into a throwaway MonoBehaviour
// or EditMode test and read the Console. Call Random.InitState(<seed>) first for
// reproducible numbers.
//
// Setup (authored in the Inspector as ScriptableObjects):
//   fish.fairMarketValue = 100
//   systemConfig.BasePriceRangeMin = 0.4, BasePriceRangeMax = 1.2   // WIDE guard-rails
//
//   deceptive : DeceptionRate = 1.0, MinOfferMultiplier = 0.5, MaxOfferMultiplier = 0.8, PatienceLevel = 2
//   honest    : DeceptionRate = 0.0, MinOfferMultiplier = 0.6, MaxOfferMultiplier = 0.95, PatienceLevel = 5
//
//   var calc = new PriceNegotiationCalculator(systemConfig);
//
// 1) Opening offers:
//   calc.GenerateOpeningOffer(deceptive, fish);  // deceiving => multiplier in [0.5, 0.8]  => ~50–80
//   calc.GenerateOpeningOffer(honest,    fish);  // honest    => multiplier in [0.85, 0.95] => ~85–95
//   => Deceptive merchant reliably opens lower than the honest one. Guard-rails (40–120)
//      never bite here, which is correct — they only catch misconfigured extremes.
//
// 2) Counter-offer evaluation (deceptive merchant, standing offer 60, ceiling = 100*0.8 = 80):
//   state = { currentMerchant=deceptive, fishBeingSold=fish, currentOffer=60, turnCount=0 }
//   EvaluateCounterOffer(state, 58);  // <= standing offer        => Accept
//   EvaluateCounterOffer(state, 62);  // within 5% of 60 (<=63)   => Accept
//   EvaluateCounterOffer(state, 75);  // in reach, deceptive roll => HoldFirm (DeceptionRate 1.0 always holds)
//   EvaluateCounterOffer(state, 95);  // above ceiling 80, turn 0 => HoldFirm
//   state.turnCount = 2;              // == PatienceLevel
//   EvaluateCounterOffer(state, 75);  // patience exhausted       => WalkAway
//
//    Same calls on the HONEST merchant (ceiling = 100*0.95 = 95, PatienceLevel 5):
//   EvaluateCounterOffer(state, 75);  // in reach, DeceptionRate 0 => RaiseOffer (honest moves up)
//   => Honest raises where the deceptive one holds firm: the deception rate is doing its job.
//
// 3) Success rate:
//   CalculateSuccessRate(95, 100);   // => 95.0   (got 95% of fair value)
//   CalculateSuccessRate(60, 100);   // => 60.0   (talked down hard — "got scammed")
//   CalculateSuccessRate(120, 100);  // => 100.0  (clamped: full value or better)
// ---------------------------------------------------------------------------------
