using System;

namespace DumyatRuntime
{
    /// <summary>
    /// A single fish item the player brings to the El Borg auction to sell.
    /// Plain serializable data carrier — public fields so it can be authored in
    /// the Inspector when referenced and read/written freely during a sale.
    /// </summary>
    [Serializable]
    public class FishData
    {
        /// <summary>Species / type of the fish (e.g. "Bouri", "Denis").</summary>
        public string fishType;

        /// <summary>
        /// The species name in Egyptian Arabic (e.g. "بوري", "دنيس"). Given to the merchant LLM so it
        /// speaks the exact local fish name instead of mistranslating the transliterated
        /// <see cref="fishType"/>. Optional — falls back to <see cref="fishType"/> when empty.
        /// </summary>
        public string fishTypeArabic;

        /// <summary>
        /// Fair market value of this fish, in game currency. All merchant offers
        /// are computed relative to this value via the merchant's offer multipliers.
        /// </summary>
        public float fairMarketValue;

        /// <summary>Weight of the fish (kg). The honest weight the player actually caught.</summary>
        public float weight;
    }

    /// <summary>
    /// One species the market recognises: its name (transliterated + Egyptian Arabic) and the weight
    /// band a fish of that species can be. The negotiation hands the merchant the whole catalog so it
    /// can ASK the fisherman what they brought, only acknowledge a species that actually exists here,
    /// and refuse a claimed weight outside that species' band — instead of assuming a fish itself.
    /// </summary>
    [Serializable]
    public class FishCatalogEntry
    {
        /// <summary>Transliterated species name (e.g. "Bouri").</summary>
        public string fishType;

        /// <summary>Species name in Egyptian Arabic (e.g. "بوري").</summary>
        public string fishTypeArabic;

        /// <summary>Smallest weight (kg) the market will buy for this species.</summary>
        public float minWeight;

        /// <summary>Largest weight (kg) the market will buy for this species.</summary>
        public float maxWeight;
    }

    /// <summary>
    /// A completed fish sale: WHICH fish (type + weight) was sold and for HOW MUCH. Produced by the
    /// negotiation flow when a deal is accepted and saved (via <c>PlayerStats</c> / the master save) so
    /// the fish-sell process — deducting the fish from inventory, crediting the money — and later
    /// inventory validation can rely on an authoritative record of every sale. Serializable so it
    /// persists through <see cref="GameSaveData"/>.
    /// </summary>
    [Serializable]
    public class FishSaleRecord
    {
        /// <summary>Transliterated species name sold (e.g. "Bouri").</summary>
        public string fishType;

        /// <summary>Species name in Egyptian Arabic (e.g. "بوري").</summary>
        public string fishTypeArabic;

        /// <summary>Weight (kg) of the fish sold — the authoritative caught weight.</summary>
        public float weight;

        /// <summary>Final agreed sale price, in whole Egyptian Pounds.</summary>
        public float agreedPrice;

        /// <summary>The fish's fair market value at the time of sale (reference for the success score).</summary>
        public float fairMarketValue;

        /// <summary>How good a deal the player got, 0–100 (agreedPrice / fairMarketValue).</summary>
        public float successRate;
    }
}
