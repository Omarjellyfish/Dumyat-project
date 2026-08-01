using UnityEngine;
using UnityEngine.Serialization;

namespace DumyatRuntime
{
    /// <summary>
    /// One authorable fish species the market can hand to the merchant: name (transliterated +
    /// Egyptian Arabic), a per-kg fair-price band and a caught-weight band. Authored as
    /// ScriptableObject assets under <c>Assets/Runtime/Content/Fish</c> so designers can add species
    /// without touching code, and collected into a <see cref="FishDatabase"/> for lookup.
    ///
    /// A caught <see cref="Fish"/> rolls its weight and its per-kg rate once from these bands; the
    /// resulting fair market value is what the merchant negotiates against.
    /// </summary>
    [CreateAssetMenu(fileName = "FishSpecies", menuName = "Dumyat/Fish/Fish Species", order = 0)]
    public class FishSpeciesSO : ScriptableObject
    {
        [Tooltip("Species name (transliterated, e.g. \"Bouri\", \"Denis\"). Kept for logs/subtitles.")]
        [SerializeField, FormerlySerializedAs("fishType")] private string _fishType = "Bouri";

        [Tooltip("Species name in Egyptian Arabic (e.g. \"بوري\"). Sent to the merchant LLM so it " +
                 "says the exact local fish name instead of mistranslating the transliteration.")]
        [SerializeField, FormerlySerializedAs("fishTypeArabic")] private string _fishTypeArabic = "بوري";

        [Tooltip("Minimum fair price per kilogram, in game currency.")]
        [SerializeField, FormerlySerializedAs("minPricePerKg")] private float _minPricePerKg = 60f;

        [Tooltip("Maximum fair price per kilogram, in game currency.")]
        [SerializeField, FormerlySerializedAs("maxPricePerKg")] private float _maxPricePerKg = 110f;

        [Tooltip("Minimum weight (kg) of a caught fish of this species.")]
        [SerializeField, FormerlySerializedAs("minWeight")] private float _minWeight = 0.4f;

        [Tooltip("Maximum weight (kg) of a caught fish of this species.")]
        [SerializeField, FormerlySerializedAs("maxWeight")] private float _maxWeight = 3.5f;

        /// <summary>Transliterated species name (e.g. "Bouri").</summary>
        public string FishType => _fishType;

        /// <summary>Species name in Egyptian Arabic (e.g. "بوري").</summary>
        public string FishTypeArabic => _fishTypeArabic;

        /// <summary>Minimum fair price per kilogram, in game currency.</summary>
        public float MinPricePerKg => _minPricePerKg;

        /// <summary>Maximum fair price per kilogram, in game currency.</summary>
        public float MaxPricePerKg => _maxPricePerKg;

        /// <summary>Smallest weight (kg) a caught fish of this species can be.</summary>
        public float MinWeight => _minWeight;

        /// <summary>Largest weight (kg) a caught fish of this species can be.</summary>
        public float MaxWeight => _maxWeight;

        /// <summary>
        /// Rolls a caught weight (kg) in this species' band. Guards an inverted or zero band so a
        /// mis-authored asset yields a small positive weight instead of an exception or a 0kg fish.
        /// </summary>
        public float RollWeight() => Random.Range(
            Mathf.Max(0.01f, _minWeight),
            Mathf.Max(_minWeight, _maxWeight));

        /// <summary>
        /// Rolls this fish's fair price per kilogram — the market rate for this catch. Guarded the
        /// same way as <see cref="RollWeight"/>.
        /// </summary>
        public float RollPricePerKg() => Random.Range(
            Mathf.Max(0f, _minPricePerKg),
            Mathf.Max(_minPricePerKg, _maxPricePerKg));

        /// <summary>This species' entry in the market catalog (name + the weight band it trades in).</summary>
        public FishCatalogEntry ToCatalogEntry() => new FishCatalogEntry
        {
            fishType = _fishType,
            fishTypeArabic = _fishTypeArabic,
            minWeight = _minWeight,
            maxWeight = _maxWeight
        };
    }
}
