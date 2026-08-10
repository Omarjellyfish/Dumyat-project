using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Global, designer-authored configuration for the whole merchant selling
    /// system at the El Borg fish market. One asset wires up every merchant and
    /// the shared market-wide tuning values. Pure data — no behaviour here.
    /// </summary>
    [CreateAssetMenu(fileName = "MerchantSystemConfig", menuName = "Dumyat/Merchant/Merchant System Config")]
    public class MerchantSystemConfig : ScriptableObject
    {
        [Header("Roster")]
        [Tooltip("Every merchant archetype available in the market.")]
        [SerializeField] private List<MerchantConfig> _merchantConfigs = new List<MerchantConfig>();

        [Header("Fair Price Range")]
        [Tooltip("DEPRECATED — no longer applied. Pricing is fish-data driven now (fractions of the " +
                 "fish's fair market value); this guard-rail clamp was removed.")]
        [SerializeField] private float _basePriceRangeMin = 0.9f;

        [Tooltip("DEPRECATED — no longer applied. Pricing is fish-data driven now (fractions of the " +
                 "fish's fair market value); this guard-rail clamp was removed.")]
        [SerializeField] private float _basePriceRangeMax = 1.1f;

        [Header("Timing")]
        [Tooltip("Cooldown, in seconds, before the player can approach a merchant " +
                 "again after a negotiation ends.")]
        [SerializeField] private float _cooldownBetweenApproaches = 5f;

        [Header("Scripted (offline) fallback")]
        [Tooltip("Market-wide dialogue bank used by the SCRIPTED (no-AI) merchant sale. A merchant may " +
                 "override this with its own bank on its MerchantConfig; when neither is set the scripted " +
                 "sale still runs using built-in fallback lines.")]
        [SerializeField] private ScriptedMerchantLinesSO _scriptedLines;

        [Header("Opening idiom")]
        [Tooltip("Market-wide bank of opening proverbs. Shared by BOTH sale paths — the AI voice flow " +
                 "speaks them, the scripted (offline) sale shows them as subtitles — so it lives here " +
                 "rather than on either controller, and the two can never drift apart. The asset itself " +
                 "decides which merchants are mild enough and how often one is actually said.")]
        [SerializeField] private MerchantIdiomsSO _idioms;

        /// <summary>Every merchant archetype available in the market.</summary>
        public IReadOnlyList<MerchantConfig> MerchantConfigs => _merchantConfigs;

        /// <summary>Lower bound of the per-sale fair-price multiplier.</summary>
        public float BasePriceRangeMin => _basePriceRangeMin;

        /// <summary>Upper bound of the per-sale fair-price multiplier.</summary>
        public float BasePriceRangeMax => _basePriceRangeMax;

        /// <summary>Cooldown in seconds before the player can approach a merchant again.</summary>
        public float CooldownBetweenApproaches => _cooldownBetweenApproaches;

        /// <summary>
        /// Market-wide scripted-sale dialogue bank (the no-AI fallback), or null if none is assigned. A
        /// <see cref="MerchantConfig.ScriptedLines"/> override takes precedence for that merchant.
        /// </summary>
        public ScriptedMerchantLinesSO ScriptedLines => _scriptedLines;

        /// <summary>
        /// Market-wide opening-idiom bank, or null if none is assigned (idioms simply never fire then).
        /// Both the AI and the scripted sale read it from here.
        /// </summary>
        public MerchantIdiomsSO Idioms => _idioms;
    }
}
