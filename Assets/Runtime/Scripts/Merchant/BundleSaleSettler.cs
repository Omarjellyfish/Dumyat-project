using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Shared payout for a scale-based bundle sale, used by BOTH the AI negotiated flow
    /// (<see cref="NegotiationFlowController"/>) and the scripted offline flow (<c>ScriptedSaleController</c>)
    /// so the money/inventory seam has exactly one implementation. For each fish still resting on the scale
    /// that is part of the agreed deal, it records the sale (<see cref="IPlayerStatsService.RecordSale"/>,
    /// which fires <see cref="FishSaleRecordedEvent"/> — the currency seam), consumes it from the player's
    /// inventory (<see cref="IFishInventory.MarkFishSold"/>), and destroys the world fish so it "disappears".
    ///
    /// Pure logic — a static helper, no Unity object of its own. Fish identity is by reference: the
    /// <see cref="FishData"/> keys in <paramref name="priceByFish"/> must be the same instances the scale
    /// hands out (<c>Fish.ToFishData()</c> caches, so they are), which the callers guarantee.
    /// </summary>
    public static class BundleSaleSettler
    {
        /// <summary>
        /// Pays out every fish on <paramref name="scale"/> that appears in <paramref name="priceByFish"/>.
        /// Returns the total paid and the number of fish sold via <paramref name="soldCount"/>. A fish on the
        /// scale but absent from the map is left untouched (it was not part of the deal).
        /// </summary>
        public static float Payout(ISellScale scale, IReadOnlyDictionary<FishData, float> priceByFish,
            IFishInventory inventory, IPlayerStatsService playerStats,
            PriceNegotiationCalculator calculator, out int soldCount)
        {
            soldCount = 0;
            float total = 0f;
            if (scale == null || priceByFish == null) return 0f;

            // Copy first — MarkFishSold / Destroy mutate the scale's live resting set as we go.
            var soldFish = new List<Fish>(scale.FishOnScale);

            foreach (Fish fish in soldFish)
            {
                if (fish == null) continue;
                FishData data = fish.ToFishData();
                if (data == null || !priceByFish.TryGetValue(data, out float price)) continue;   // not in the deal

                float fair = data.fairMarketValue;
                var record = new FishSaleRecord
                {
                    fishType = data.fishType,
                    fishTypeArabic = data.fishTypeArabic,
                    weight = data.weight,
                    agreedPrice = Mathf.Round(price),
                    fairMarketValue = fair,
                    successRate = calculator != null ? calculator.CalculateSuccessRate(price, fair) : 0f
                };

                playerStats?.RecordSale(record);        // currency seam: fires FishSaleRecordedEvent per fish
                total += record.agreedPrice;
                soldCount++;

                inventory?.MarkFishSold(data);          // removes from inventory + destroys the tracked world fish
                if (fish != null) Object.Destroy(fish.gameObject);   // safety if the fish was not inventory-tracked
            }

            Debug.Log($"[BundleSale] SOLD {soldCount} fish for {total:0} EGP total (removed from the scale).");
            return total;
        }
    }
}
