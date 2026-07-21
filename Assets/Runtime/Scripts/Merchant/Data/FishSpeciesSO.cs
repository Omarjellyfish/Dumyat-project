using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// One authorable fish species the market can hand to the merchant: name (transliterated +
    /// Egyptian Arabic), a per-kg fair-price band and a caught-weight band. This is the same data
    /// that used to be hardcoded as <c>RandomFishInventoryTestDriver.FishSpecies</c> rows — moved to
    /// ScriptableObject assets so designers can author species without touching code. The test
    /// inventory still rolls a random weight/price in these bands per sale (behaviour unchanged); only
    /// the data representation moved.
    /// </summary>
    [CreateAssetMenu(fileName = "FishSpecies", menuName = "Dumyat/Fish Species", order = 0)]
    public class FishSpeciesSO : ScriptableObject
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
}
