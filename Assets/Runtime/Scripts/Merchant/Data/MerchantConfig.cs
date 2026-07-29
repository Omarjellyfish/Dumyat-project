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
        [Tooltip("DEPRECATED — no longer affects price. Pricing is now driven purely by fish data " +
                 "(fractions of the fish's fair market value in PriceNegotiationCalculator). This " +
                 "merchant archetype only influences the deception roll and patience now.")]
        [SerializeField] private float _minOfferMultiplier = 0.5f;

        [Tooltip("DEPRECATED — no longer affects price. Pricing is now driven purely by fish data " +
                 "(fractions of the fish's fair market value in PriceNegotiationCalculator). This " +
                 "merchant archetype only influences the deception roll and patience now.")]
        [SerializeField] private float _maxOfferMultiplier = 0.9f;

        [Header("Behaviour")]
        [Tooltip("Probability (0-1) that the merchant lowballs / deceives on a given " +
                 "offer, e.g. 0.7 = 70% chance to lowball. Deception is price/weight only.")]
        [Range(0f, 1f)]
        [SerializeField] private float _deceptionRate = 0.7f;

        [Tooltip("How many player counter-offers the merchant tolerates before walking " +
                 "away from the negotiation.")]
        [SerializeField] private int _patienceLevel = 3;

        [Header("Scripted (offline) fallback")]
        [Tooltip("OPTIONAL per-merchant dialogue bank for the SCRIPTED (no-AI) sale. Leave empty to use " +
                 "the market-wide bank on MerchantSystemConfig. Set it only to give THIS merchant its own " +
                 "distinct scripted lines.")]
        [SerializeField] private ScriptedMerchantLinesSO _scriptedLines;

        [Header("Voice")]
        // The ElevenLabs voice every NPC using this config speaks with — MerchantNPC reads its
        // AssignedVoiceId straight from here. The archetype owns the voice; there is no per-NPC
        // override and no shared pool.
        // IMPORTANT: any value here MUST be a verified Egyptian Arabic voice (اللهجة المصرية), not
        // generic "Arabic", Gulf, Levantine, Maghrebi or MSA. See docs/MERCHANT_CONTEXT.md.
        [Tooltip("The ElevenLabs voice every NPC using this config speaks with. " +
                 "MUST be a verified Egyptian Arabic voice. See docs/MERCHANT_CONTEXT.md.")]
        [SerializeField] private string _voiceId;

        [Header("Voice (TTS parameters)")]
        // Per-merchant ElevenLabs voice_settings, so each archetype can sound distinct while still
        // sharing the pooled synthesis path. These override the shared ElevenLabsClient's defaults for
        // THIS merchant's lines only. The two model generations take DIFFERENT parameters, so which
        // group below applies is chosen by the version selector; the other group is ignored.
        [Tooltip("Which ElevenLabs model generation this merchant speaks with. " +
                 "v2 = speed/stability/similarity/style; v3 = stability + language override (more expressive).")]
        [SerializeField] private ElevenLabsModelVersion _ttsVersion = ElevenLabsModelVersion.V2;

        [Header("Voice — Eleven v2 parameters")]
        [Tooltip("Speaking speed for this merchant. 1 = normal, < 1 slower, > 1 faster. " +
                 "ElevenLabs only accepts 0.7–1.2.")]
        [Range(0.7f, 1.2f)]
        [SerializeField] private float _v2Speed = 1f;

        [Tooltip("Stability. Lower = more variable/expressive, higher = more stable/consistent.")]
        [Range(0f, 1f)]
        [SerializeField] private float _v2Stability = 0.5f;

        [Tooltip("Similarity boost. How closely the output matches the original voice.")]
        [Range(0f, 1f)]
        [SerializeField] private float _v2Similarity = 0.75f;

        [Tooltip("Style exaggeration. 0 = neutral, 1 = highly expressive. Over ~0.5 may add instability. " +
                 "Only affects v2 models.")]
        [Range(0f, 1f)]
        [SerializeField] private float _v2Style = 0f;

        [Header("Voice — Eleven v3 parameters")]
        [Tooltip("Stability for Eleven v3. v3 accepts only 0 = Creative (most expressive), 0.5 = Natural, " +
                 "1 = Robust (most stable); authored values are snapped to the nearest.")]
        [Range(0f, 1f)]
        [SerializeField] private float _v3Stability = 0.5f;

        [Tooltip("Language override for Eleven v3 (ISO 639-1, e.g. 'ar' for Arabic). Leave BLANK to auto-detect.")]
        [SerializeField] private string _v3LanguageOverride = "ar";

        /// <summary>Display name of the merchant, used in subtitles and UI.</summary>
        public string MerchantName => _merchantName;

        /// <summary>
        /// Personality blurb injected into the LLM system prompt to differentiate
        /// this merchant. Should read as warm-but-shrewd, never villainous.
        /// </summary>
        public string Personality => _personality;

        /// <summary>DEPRECATED — no longer used for pricing (fish-data driven now). Kept for API stability.</summary>
        public float MinOfferMultiplier => _minOfferMultiplier;

        /// <summary>DEPRECATED — no longer used for pricing (fish-data driven now). Kept for API stability.</summary>
        public float MaxOfferMultiplier => _maxOfferMultiplier;

        /// <summary>Chance (0-1) the merchant lowballs / deceives on a given offer.</summary>
        public float DeceptionRate => _deceptionRate;

        /// <summary>
        /// This merchant's own scripted-sale dialogue bank, or null to fall back to the market-wide bank on
        /// <see cref="MerchantSystemConfig.ScriptedLines"/>. Used by the no-AI offline sale only.
        /// </summary>
        public ScriptedMerchantLinesSO ScriptedLines => _scriptedLines;

        /// <summary>Number of player counter-offers tolerated before walking away.</summary>
        public int PatienceLevel => _patienceLevel;

        /// <summary>
        /// The ElevenLabs voice every NPC using this config speaks with (the archetype owns its
        /// voice — there is no per-NPC override or shared pool). Any value MUST be a verified
        /// Egyptian Arabic voice. See docs/MERCHANT_CONTEXT.md.
        /// </summary>
        public string VoiceId => _voiceId;

        /// <summary>Which ElevenLabs model generation this merchant's voice lines use.</summary>
        public ElevenLabsModelVersion TtsVersion => _ttsVersion;

        /// <summary>
        /// This merchant's ElevenLabs voice-synthesis parameters, bundled for the TTS chain and built
        /// for the selected <see cref="TtsVersion"/> (v2 → speed/stability/similarity/style; v3 →
        /// stability + language override). Passed through <see cref="ITTSService.PlaySpeech"/> so each
        /// merchant sounds distinct; the client still clamps values to ElevenLabs' accepted ranges.
        /// </summary>
        public TtsVoiceSettings VoiceSettings =>
            _ttsVersion == ElevenLabsModelVersion.V3
                ? TtsVoiceSettings.ForV3(_v3Stability, _v3LanguageOverride)
                : TtsVoiceSettings.ForV2(_v2Speed, _v2Stability, _v2Similarity, _v2Style);
    }
}
