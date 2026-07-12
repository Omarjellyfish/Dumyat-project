using System;
using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// TEST-ONLY <see cref="IFishInventory"/>: hands the negotiation flow a fresh, randomized
    /// <see cref="FishData"/> per sale so the merchant pipeline can be exercised without the real
    /// inventory. Each entry is an Egyptian species with a fair-value and weight RANGE; a sale rolls
    /// a weight in range and derives the fair market value from a per-kg price so heavier fish are
    /// worth more. Pure data generation — no ItemData / InventoryManager coupling.
    /// </summary>
    public class RandomFishInventoryTestDriver : MonoBehaviour, IFishInventory
    {
        [Header("Species Pool")]
        [Tooltip("Egyptian fish species the test can hand to the merchant, authored as FishSpeciesSO " +
                 "assets. Each sale picks one at random. Assign the assets under Assets/Runtime/Content/Fish.")]
        [SerializeField]
        private List<FishSpeciesSO> _species = new List<FishSpeciesSO>();

        [Header("Value")]
        [Tooltip("Round the derived fair market value to the nearest whole currency unit for cleaner prompts.")]
        [SerializeField] private bool _roundValue = true;

        /// <inheritdoc/>
        public bool HasFish => _species != null && _species.Count > 0;

        /// <inheritdoc/>
        public IReadOnlyList<FishCatalogEntry> Catalog
        {
            get
            {
                var catalog = new List<FishCatalogEntry>();
                if (_species == null) return catalog;
                foreach (FishSpeciesSO s in _species)
                {
                    if (s == null || string.IsNullOrWhiteSpace(s.fishType)) continue;
                    catalog.Add(new FishCatalogEntry
                    {
                        fishType = s.fishType,
                        fishTypeArabic = s.fishTypeArabic,
                        minWeight = s.minWeight,
                        maxWeight = s.maxWeight
                    });
                }
                return catalog;
            }
        }

        /// <inheritdoc/>
        // Test provider rolls a fresh random fish per sale and holds no persistent stock, so there is
        // nothing to remove — just log the sale so the seam is observable in the Console.
        public void MarkFishSold(FishData fish)
        {
            if (fish == null) return;
            Debug.Log($"[FishInventory] Fish SOLD (test provider, nothing to remove): " +
                      $"{fish.fishType} {fish.weight:0.##}kg.");
        }

        /// <inheritdoc/>
        public bool TryGetFishToSell(out FishData fish)
        {
            fish = null;
            if (!HasFish) return false;

            FishSpeciesSO species = _species[UnityEngine.Random.Range(0, _species.Count)];
            if (species == null || string.IsNullOrWhiteSpace(species.fishType)) return false;

            float weight = UnityEngine.Random.Range(
                Mathf.Max(0.01f, species.minWeight),
                Mathf.Max(species.minWeight, species.maxWeight));
            float pricePerKg = UnityEngine.Random.Range(
                Mathf.Max(0f, species.minPricePerKg),
                Mathf.Max(species.minPricePerKg, species.maxPricePerKg));

            float value = weight * pricePerKg;
            if (_roundValue) value = Mathf.Round(value);

            fish = new FishData
            {
                fishType = species.fishType,
                fishTypeArabic = species.fishTypeArabic,
                weight = (float)Math.Round(weight, 2),
                fairMarketValue = value
            };

            Debug.Log($"[FishInventory] Providing {fish.fishType} ({fish.fishTypeArabic}) — " +
                      $"{fish.weight:0.##}kg, fair value {fish.fairMarketValue:0.#}.");
            return true;
        }
    }
}
