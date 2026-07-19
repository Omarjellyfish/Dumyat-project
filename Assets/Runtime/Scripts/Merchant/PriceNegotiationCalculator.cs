using System.Collections.Generic;
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
        //
        // PRICING IS DRIVEN BY FISH DATA (the fish's fairMarketValue), NOT by merchant archetype
        // multipliers. The merchant archetype only affects the DECEPTION ROLL and PATIENCE now; the
        // opening offer and the negotiation ceiling are both fractions of the fish's fair value below.
        // The deception roll picks which band (honest vs deceptive) those fractions come from.

        /// <summary>HONEST opening offer as a fraction of the fish's fair market value: [min, max].</summary>
        private const float HonestOpeningMin = 0.65f;
        private const float HonestOpeningMax = 0.80f;

        /// <summary>DECEPTIVE (lowball) opening offer as a fraction of fair value: [min, max].</summary>
        private const float DeceptiveOpeningMin = 0.40f;
        private const float DeceptiveOpeningMax = 0.60f;

        /// <summary>
        /// HONEST negotiation ceiling as a fraction of fair value: an honest merchant can be argued
        /// all the way up to the fish's FULL fair market value (1.0).
        /// </summary>
        private const float HonestMaxFraction = 1.0f;

        /// <summary>
        /// DECEPTIVE negotiation ceiling as a fraction of fair value: [min, max]. A lowballed
        /// negotiation can never be haggled up to full fair value — it caps meaningfully below it.
        /// </summary>
        private const float DeceptiveMaxMin = 0.75f;
        private const float DeceptiveMaxMax = 0.90f;

        /// <summary>
        /// If the player's demand is within this fraction of the merchant's standing
        /// offer, the merchant just accepts rather than haggling further.
        /// </summary>
        private const float AcceptGapFraction = 0.05f;

        /// <summary>
        /// Fraction of the opening→ceiling gap a single RaiseOffer may cover. 0.35 means the
        /// merchant needs roughly three concessions to travel the whole envelope, so a big gap is
        /// climbed gradually instead of in one jump.
        /// </summary>
        private const float RaiseStepFraction = 0.35f;

        /// <summary>
        /// Hard cap on how far, PER KILOGRAM, the merchant's negotiation ceiling may sit above its
        /// opening offer. The whole opening→max climb can never exceed this many EGP for each kilo of
        /// fish, so a heavy/valuable catch still can't be argued up by an unbounded amount.
        /// </summary>
        private const float MaxEnvelopePerKg = 100f;

        /// <param name="systemConfig">
        /// No longer used. Pricing is now purely fish-data driven (fractions of the fish's fair
        /// market value), so the old fair-price guard-rail multipliers are not applied — with typical
        /// values they would clamp away the intended lowballing. Kept on the signature for API stability.
        /// </param>
        public PriceNegotiationCalculator(MerchantSystemConfig systemConfig = null)
        {
            // systemConfig intentionally ignored — see the param note above.
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
        /// Produces the merchant's first offer for a fish with a caller-supplied deception decision.
        /// The offer is purely FISH-DATA driven: a fraction of the fish's <see cref="FishData.fairMarketValue"/>
        /// — an honest merchant opens in [<see cref="HonestOpeningMin"/>, <see cref="HonestOpeningMax"/>],
        /// a deceiving one lowballs in [<see cref="DeceptiveOpeningMin"/>, <see cref="DeceptiveOpeningMax"/>].
        /// The merchant archetype does NOT influence the amount (only the deception roll). Clamped to
        /// the global guard-rails when a system config is present. <paramref name="merchant"/> is kept
        /// on the signature for API stability but is unused here.
        /// </summary>
        public float GenerateOpeningOffer(MerchantConfig merchant, FishData fish, bool deceiving)
        {
            if (fish == null)
            {
                Debug.LogWarning("PriceNegotiationCalculator: null fish in GenerateOpeningOffer.");
                return 0f;
            }

            float fraction = deceiving
                ? RandomInRange(DeceptiveOpeningMin, DeceptiveOpeningMax)
                : RandomInRange(HonestOpeningMin, HonestOpeningMax);

            return fish.fairMarketValue * fraction;
        }

        /// <summary>
        /// The ABSOLUTE ceiling the merchant will pay for this fish THIS negotiation — decided once at
        /// the opening from the SAME deception roll as the opening offer, and enforced against every
        /// subsequent offer (offers only move UP toward it, never past it). Purely FISH-DATA driven: a
        /// fraction of the fish's fair value — an HONEST merchant can be argued all the way up to the
        /// fish's FULL fair value (<see cref="HonestMaxFraction"/> = 1.0); a DECEPTIVE one caps below
        /// it (<see cref="DeceptiveMaxMin"/>..<see cref="DeceptiveMaxMax"/>). The merchant archetype
        /// does NOT influence the amount. Never below <paramref name="openingOffer"/>, so the envelope
        /// is always valid. <paramref name="merchant"/> is kept for API stability but is unused here.
        /// </summary>
        public float GenerateMaxOffer(MerchantConfig merchant, FishData fish, bool deceiving, float openingOffer)
        {
            if (fish == null)
            {
                Debug.LogWarning("PriceNegotiationCalculator: null fish in GenerateMaxOffer.");
                return openingOffer;
            }

            float fraction = deceiving
                ? RandomInRange(DeceptiveMaxMin, DeceptiveMaxMax)
                : HonestMaxFraction;

            float max = fish.fairMarketValue * fraction;

            // Cap the opening→max climb at MaxEnvelopePerKg per kilo, so a heavy or high-value catch
            // can't be haggled up by more than a bounded amount. Uses the honest caught weight.
            if (fish.weight > 0f)
            {
                float envelopeCap = openingOffer + MaxEnvelopePerKg * fish.weight;
                max = Mathf.Min(max, envelopeCap);
            }

            return Mathf.Max(max, openingOffer);
        }

        /// <summary>
        /// The most a single RaiseOffer may add on top of the standing offer, for this
        /// negotiation's opening→ceiling envelope: a <see cref="RaiseStepFraction"/> of the gap,
        /// never less than 1 EGP (so tiny envelopes can still move). The LLM is told this cap each
        /// turn and any raise beyond it is rejected — the climb to the ceiling is always gradual.
        /// </summary>
        public float GenerateMaxRaiseStep(float openingOffer, float maxOffer)
        {
            return Mathf.Max(1f, Mathf.Round((maxOffer - openingOffer) * RaiseStepFraction));
        }

        // --- Bundle (multi-fish) pricing -------------------------------------------
        //
        // The scale-based sale haggles the WHOLE catch as one total. Because the per-fish math above is
        // stateless, the bundle envelope is simply the sum of the individual fish envelopes under ONE
        // shared deception roll — no new pricing rules, just aggregation. The final per-fish prices are
        // split back out of the agreed total by fair value (see SplitBundlePrice) for the summary/payout.

        /// <summary>
        /// Computes the whole-bundle opening offer and ceiling in a single pass: each fish's opening
        /// offer and its per-fish ceiling (derived from that same opening) are summed, so the bundle
        /// envelope equals the sum of the individual fish envelopes under one shared
        /// <paramref name="deceiving"/> roll. Both outputs are 0 for an empty/null bundle.
        /// </summary>
        public void GenerateBundleEnvelope(MerchantConfig merchant, IReadOnlyList<FishData> bundle,
            bool deceiving, out float openingOffer, out float maxOffer)
        {
            openingOffer = 0f;
            maxOffer = 0f;

            if (bundle == null || bundle.Count == 0)
            {
                Debug.LogWarning("PriceNegotiationCalculator: empty bundle in GenerateBundleEnvelope.");
                return;
            }

            foreach (FishData fish in bundle)
            {
                if (fish == null) continue;
                float opening = GenerateOpeningOffer(merchant, fish, deceiving);
                openingOffer += opening;
                maxOffer += GenerateMaxOffer(merchant, fish, deceiving, opening);
            }
        }

        /// <summary>
        /// Splits an agreed BUNDLE total across the fish by each fish's fair market value (higher-value
        /// fish carry more of the price). Each share is rounded, and the rounding remainder is assigned
        /// to the highest-value fish so the per-fish prices sum EXACTLY to the (rounded)
        /// <paramref name="agreedTotal"/>. Returns one price per entry, aligned to <paramref name="bundle"/>'s
        /// order (0 for a null entry). When no fish has a positive fair value, the total is split evenly.
        /// </summary>
        public float[] SplitBundlePrice(IReadOnlyList<FishData> bundle, float agreedTotal)
        {
            int n = bundle?.Count ?? 0;
            var prices = new float[n];
            if (n == 0) return prices;

            float total = Mathf.Round(agreedTotal);

            float sumFair = 0f;
            int count = 0;
            for (int i = 0; i < n; i++)
                if (bundle[i] != null) { sumFair += bundle[i].fairMarketValue; count++; }
            if (count == 0) return prices;

            int biggest = -1;
            float running = 0f;
            for (int i = 0; i < n; i++)
            {
                FishData fish = bundle[i];
                if (fish == null) continue;

                float share = sumFair > 0f ? total * (fish.fairMarketValue / sumFair) : total / count;
                prices[i] = Mathf.Round(share);
                running += prices[i];

                if (biggest < 0 || fish.fairMarketValue > bundle[biggest].fairMarketValue) biggest = i;
            }

            // Absorb the rounding remainder into the highest-value fish so the parts sum to the total.
            if (biggest >= 0) prices[biggest] += total - running;
            return prices;
        }

        /// <summary>
        /// Decides how the merchant reacts to the player's counter-offer. Reads
        /// <see cref="NegotiationState"/> but does NOT mutate it (the caller owns turn
        /// advancement and applying any new offer).
        ///
        /// Rules, in order:
        /// 1. Player asking for ≤ the standing offer → <see cref="NegotiationOutcome.Accept"/>.
        /// 2. Turns taken ≥ patience → <see cref="NegotiationOutcome.WalkAway"/>.
        /// 3. Demand above the negotiation ceiling (state.maxOffer, else the fish's full fair value)
        ///    → walk away if patience is nearly spent, else hold firm — so low-patience merchants bail
        ///    faster on repeated high demands.
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
            // Fish-data driven ceiling: the negotiation's actual max (set at the opening), else the
            // fish's full fair market value. Merchant multipliers no longer influence price.
            float ceiling = state.maxOffer > 0f ? state.maxOffer : state.fishBeingSold.fairMarketValue;

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
        // the bounds first (a misconfigured range could invert them).
        private static float RandomInRange(float a, float b)
        {
            return a <= b ? Random.Range(a, b) : Random.Range(b, a);
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
//   fish.fairMarketValue = 100,  fish.weight = 2   // pricing is FISH-DATA driven now
//
//   deceptive : DeceptionRate = 1.0, PatienceLevel = 2   // Min/MaxOfferMultiplier no longer used
//   honest    : DeceptionRate = 0.0, PatienceLevel = 5
//
//   var calc = new PriceNegotiationCalculator();   // systemConfig no longer needed
//
// 1) Opening offers (fractions of the FISH's fair value; merchant archetype does NOT change these):
//   calc.GenerateOpeningOffer(m, fish, deceiving:true );  // => fair * [0.40, 0.60] => ~40–60
//   calc.GenerateOpeningOffer(m, fish, deceiving:false);  // => fair * [0.65, 0.80] => ~65–80
//   => Deceptive opens lower than honest. No guard-rail clamp — the fractions ARE the bounds.
//
//   Max offer (ceiling), also fish-driven:
//   calc.GenerateMaxOffer(m, fish, deceiving:false, opening);  // honest => fair * 1.0 = 100 (full fair value!)
//   calc.GenerateMaxOffer(m, fish, deceiving:true,  opening);  // deceptive => fair * [0.75, 0.90] => ~75–90
//   (both also capped so max <= opening + 100/kg * weight)
//
// 2) Counter-offer evaluation (deceptive merchant, standing offer 50, ceiling = state.maxOffer = 85):
//   state = { currentMerchant=deceptive, fishBeingSold=fish, currentOffer=50, maxOffer=85, turnCount=0 }
//   EvaluateCounterOffer(state, 48);  // <= standing offer        => Accept
//   EvaluateCounterOffer(state, 52);  // within 5% of 50 (<=52.5) => Accept
//   EvaluateCounterOffer(state, 70);  // in reach, deceptive roll => HoldFirm (DeceptionRate 1.0 always holds)
//   EvaluateCounterOffer(state, 95);  // above ceiling 85, turn 0 => HoldFirm
//   state.turnCount = 2;              // == PatienceLevel
//   EvaluateCounterOffer(state, 70);  // patience exhausted       => WalkAway
//
//    HONEST merchant (maxOffer = 100 = full fair value, PatienceLevel 5):
//   EvaluateCounterOffer(state, 70);  // in reach, DeceptionRate 0 => RaiseOffer (honest moves up)
//   => Honest raises where the deceptive one holds firm: the deception rate is doing its job.
//
// 3) Success rate:
//   CalculateSuccessRate(95, 100);   // => 95.0   (got 95% of fair value)
//   CalculateSuccessRate(60, 100);   // => 60.0   (talked down hard — "got scammed")
//   CalculateSuccessRate(120, 100);  // => 100.0  (clamped: full value or better)
// ---------------------------------------------------------------------------------
