using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Holds each merchant's flat TAKE-IT-OR-LEAVE-IT rate for the scripted (offline) sale. A merchant's
    /// rate is its "mood today": rolled ONCE the first time it is asked for and cached, so re-approaching
    /// the same merchant with the same catch always quotes the same number (no reroll-farming), while
    /// different merchants quote differently, so shopping around pays off.
    ///
    /// The rate is a fraction of fair market value drawn from the SAME honest/deceptive bands the negotiated
    /// flow uses (see <see cref="PriceNegotiationCalculator.GenerateFlatRate"/>), coloured by the merchant's
    /// <see cref="MerchantConfig.DeceptionRate"/> — so a scripted quote sits inside the range a player could
    /// have haggled to. A quote is then <c>rate × Σ fish.fairMarketValue</c>: because the total is always the
    /// rate times the CURRENT fair sum (never an accumulation of per-fish deltas), adding a fish and taking
    /// it back returns to the identical total.
    ///
    /// Plain C# — owned by <c>ScriptedSaleController</c>, not a MonoBehaviour or a DI service.
    /// </summary>
    public class ScriptedPriceBook
    {
        // Where in the opening→ceiling envelope a scripted rate lands (0 = the merchant's opening lowball,
        // 1 = the ceiling it could have been argued up to). A mid-band spread keeps quotes fair but varied.
        private const float MinEnvelopePosition = 0.35f;
        private const float MaxEnvelopePosition = 0.75f;

        private readonly PriceNegotiationCalculator _calculator;
        private readonly Dictionary<MerchantNPC, float> _rateByMerchant = new Dictionary<MerchantNPC, float>();

        public ScriptedPriceBook(PriceNegotiationCalculator calculator)
        {
            _calculator = calculator;
        }

        /// <summary>
        /// The merchant's flat rate (fraction of fair value), rolled once and cached. A merchant with no
        /// <see cref="MerchantConfig"/> gets a neutral honest roll rather than throwing.
        /// </summary>
        public float RateFor(MerchantNPC merchant)
        {
            if (merchant == null) return 1f;
            if (_rateByMerchant.TryGetValue(merchant, out float cached)) return cached;

            bool deceiving = _calculator.RollDeception(merchant.Config);   // false when Config is null
            float position = Random.Range(MinEnvelopePosition, MaxEnvelopePosition);
            float rate = _calculator.GenerateFlatRate(deceiving, position);

            _rateByMerchant[merchant] = rate;
            Debug.Log($"[ScriptedSale] '{NameOf(merchant)}' flat rate rolled: {rate:0.###} " +
                      $"(deceptive={deceiving}, envelope pos {position:0.##}).");
            return rate;
        }

        /// <summary>
        /// The whole-catch quote for <paramref name="merchant"/>: its cached rate × the summed fair value of
        /// <paramref name="bundle"/>, rounded to whole pounds. Zero for an empty/null bundle.
        /// </summary>
        public float QuoteFor(MerchantNPC merchant, IReadOnlyList<FishData> bundle)
        {
            if (bundle == null || bundle.Count == 0) return 0f;

            float fairSum = 0f;
            foreach (FishData fish in bundle)
                if (fish != null) fairSum += fish.fairMarketValue;

            return Mathf.Round(RateFor(merchant) * fairSum);
        }

        private static string NameOf(MerchantNPC merchant)
        {
            if (merchant == null) return "<none>";
            return merchant.Config != null ? merchant.Config.MerchantName : merchant.name;
        }
    }
}
