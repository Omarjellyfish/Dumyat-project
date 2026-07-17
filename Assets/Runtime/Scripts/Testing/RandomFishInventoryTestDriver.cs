using System;
using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// TEST-ONLY <see cref="IFishInventory"/>: hands the negotiation flow a fresh, randomized
    /// <see cref="FishData"/> per sale so the merchant pipeline can be exercised without fishing.
    /// A sale rolls a weight and a per-kg rate in the species' bands, deriving a fair market value so
    /// heavier fish are worth more — the same rolls a real <see cref="Fish"/> makes at spawn.
    ///
    /// For the real thing — the fish the player actually caught — use
    /// <see cref="CaughtFishInventory"/>. Keep only one of the two in a scene, or assign the one you
    /// want explicitly on <c>NegotiationFlowController</c> / <c>MerchantApproachCoordinator</c>: they resolve an
    /// inventory by scanning the scene and would otherwise pick whichever they find first.
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
                foreach (FishSpeciesSO species in _species)
                {
                    if (species == null || string.IsNullOrWhiteSpace(species.FishType)) continue;
                    catalog.Add(species.ToCatalogEntry());
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
            if (species == null || string.IsNullOrWhiteSpace(species.FishType)) return false;

            float weight = species.RollWeight();
            float value = weight * species.RollPricePerKg();
            if (_roundValue) value = Mathf.Round(value);

            fish = new FishData
            {
                fishType = species.FishType,
                fishTypeArabic = species.FishTypeArabic,
                weight = (float)Math.Round(weight, 2),
                fairMarketValue = value
            };

            Debug.Log($"[FishInventory] Providing {fish.fishType} ({fish.fishTypeArabic}) — " +
                      $"{fish.weight:0.##}kg, fair value {fish.fairMarketValue:0.#}.");
            return true;
        }
    }
}
