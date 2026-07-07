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
}
