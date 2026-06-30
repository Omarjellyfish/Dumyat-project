using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Designer-authored archetype for a single fish-market merchant NPC at the
    /// El Borg auction. Drives the merchant's name, LLM personality, haggling
    /// economics and Egyptian Arabic voice. Pure data — no behaviour here.
    /// </summary>
    [CreateAssetMenu(fileName = "MerchantConfig", menuName = "Dumyat/Merchant/Merchant Config")]
    public class MerchantConfig : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Display name of the merchant (e.g. for subtitles / UI).")]
        [SerializeField] private string _merchantName;

        [Tooltip("Personality description injected into the LLM system prompt. " +
                 "Warm-but-shrewd market vendor — theatrical and self-interested, " +
                 "NOT a villain or scammer archetype. See docs/MERCHANT_CONTEXT.md.")]
        [TextArea(3, 8)]
        [SerializeField] private string _personality;

        [Header("Offer Economics")]
        [Tooltip("Lowest opening offer as a multiplier of the fish's fair market value " +
                 "(e.g. 0.5 = offers half of fair value).")]
        [SerializeField] private float _minOfferMultiplier = 0.5f;

        [Tooltip("Highest the merchant is willing to go, as a multiplier of the fish's " +
                 "fair market value (e.g. 0.9 = caps out at 90% of fair value).")]
        [SerializeField] private float _maxOfferMultiplier = 0.9f;

        [Header("Behaviour")]
        [Tooltip("Probability (0-1) that the merchant lowballs / deceives on a given " +
                 "offer, e.g. 0.7 = 70% chance to lowball. Deception is price/weight only.")]
        [Range(0f, 1f)]
        [SerializeField] private float _deceptionRate = 0.7f;

        [Tooltip("How many player counter-offers the merchant tolerates before walking " +
                 "away from the negotiation.")]
        [SerializeField] private int _patienceLevel = 3;

        [Header("Voice (optional override)")]
        // OPTIONAL per-archetype override. Leave EMPTY (the normal path) to have MerchantSpawner
        // assign a voice from the shared VoicePool at spawn. If set, this exact voice is always
        // used for this archetype.
        // IMPORTANT: any value here MUST be a verified Egyptian Arabic voice (اللهجة المصرية), not
        // generic "Arabic", Gulf, Levantine, Maghrebi or MSA. See docs/MERCHANT_CONTEXT.md.
        [Tooltip("OPTIONAL ElevenLabs voice override. Empty = assigned from the VoicePool at spawn. " +
                 "Any value MUST be a verified Egyptian Arabic voice. See docs/MERCHANT_CONTEXT.md.")]
        [SerializeField] private string _voiceId;

        /// <summary>Display name of the merchant, used in subtitles and UI.</summary>
        public string MerchantName => _merchantName;

        /// <summary>
        /// Personality blurb injected into the LLM system prompt to differentiate
        /// this merchant. Should read as warm-but-shrewd, never villainous.
        /// </summary>
        public string Personality => _personality;

        /// <summary>Lowest opening offer as a multiplier of fair fish value.</summary>
        public float MinOfferMultiplier => _minOfferMultiplier;

        /// <summary>Maximum offer the merchant will reach, as a multiplier of fair fish value.</summary>
        public float MaxOfferMultiplier => _maxOfferMultiplier;

        /// <summary>Chance (0-1) the merchant lowballs / deceives on a given offer.</summary>
        public float DeceptionRate => _deceptionRate;

        /// <summary>Number of player counter-offers tolerated before walking away.</summary>
        public int PatienceLevel => _patienceLevel;

        /// <summary>
        /// Optional per-archetype ElevenLabs voice override. Empty means the merchant draws a voice
        /// from the shared <see cref="VoicePool"/> at spawn (see MerchantNPC.AssignedVoiceId). Any
        /// value MUST be a verified Egyptian Arabic voice. See docs/MERCHANT_CONTEXT.md.
        /// </summary>
        public string VoiceId => _voiceId;
    }
}
