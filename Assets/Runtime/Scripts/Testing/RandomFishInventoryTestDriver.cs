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
        /// <summary>One authorable species row: name + per-kg price band + weight band.</summary>
        [Serializable]
        public class FishSpecies
        {
            [Tooltip("Species name (transliterated, e.g. \"Bouri\", \"Denis\"). Kept for logs/subtitles.")]
            public string fishType = "Bouri";

            [Tooltip("Species name in Egyptian Arabic (e.g. \"بوري\"). Sent to the merchant LLM so it " +
                     "says the exact local fish name instead of mistranslating the transliteration.")]
            public string fishTypeArabic = "بوري";

            [Tooltip("Minimum fair price per kilogram, in game currency.")]
            public float minPricePerKg = 60f;

            [Tooltip("Maximum fair price per kilogram, in game currency.")]
            public float maxPricePerKg = 110f;

            [Tooltip("Minimum weight (kg) of a caught fish of this species.")]
            public float minWeight = 0.4f;

            [Tooltip("Maximum weight (kg) of a caught fish of this species.")]
            public float maxWeight = 3.5f;
        }

        [Header("Species Pool")]
        [Tooltip("Egyptian fish species the test can hand to the merchant. Each sale picks one at random.")]
        [SerializeField]
        private List<FishSpecies> _species = new List<FishSpecies>
        {
            new FishSpecies { fishType = "Bouri",    fishTypeArabic = "بوري",    minPricePerKg = 55f,  maxPricePerKg = 90f,  minWeight = 0.4f, maxWeight = 2.5f }, // grey mullet
            new FishSpecies { fishType = "Denis",    fishTypeArabic = "دنيس",    minPricePerKg = 120f, maxPricePerKg = 200f, minWeight = 0.3f, maxWeight = 1.8f }, // sea bream
            new FishSpecies { fishType = "Qaroos",   fishTypeArabic = "قاروص",   minPricePerKg = 130f, maxPricePerKg = 220f, minWeight = 0.5f, maxWeight = 3.0f }, // sea bass
            new FishSpecies { fishType = "Wagar",    fishTypeArabic = "وقار",    minPricePerKg = 150f, maxPricePerKg = 260f, minWeight = 0.8f, maxWeight = 4.5f }, // grouper
            new FishSpecies { fishType = "Balamida", fishTypeArabic = "بلاميدة", minPricePerKg = 45f,  maxPricePerKg = 80f,  minWeight = 0.6f, maxWeight = 3.0f }, // bonito
            new FishSpecies { fishType = "Subaity",  fishTypeArabic = "سبيطي",   minPricePerKg = 90f,  maxPricePerKg = 160f, minWeight = 0.3f, maxWeight = 1.5f }, // sea bream (sobaity)
        };

        [Header("Value")]
        [Tooltip("Round the derived fair market value to the nearest whole currency unit for cleaner prompts.")]
        [SerializeField] private bool _roundValue = true;

        /// <inheritdoc/>
        public bool HasFish => _species != null && _species.Count > 0;

        /// <inheritdoc/>
        public bool TryGetFishToSell(out FishData fish)
        {
            fish = null;
            if (!HasFish) return false;

            FishSpecies species = _species[UnityEngine.Random.Range(0, _species.Count)];
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
