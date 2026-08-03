using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Credentials and connection settings for the Munsit realtime speech-to-text service
    /// (see <see cref="MunsitSTTClient"/>). Munsit is Arabic-first and covers 25+ dialects with no
    /// dialect parameter, which is why it is the preferred provider for the Egyptian-Arabic merchant
    /// negotiation — see docs/MERCHANT_CONTEXT.md.
    /// </summary>
    [CreateAssetMenu(fileName = "MunsitConfig", menuName = "Dumyat/AI/Munsit Config")]
    public class MunsitConfig : ScriptableObject
    {
        // Effective defaults, also used as the fallback when a field is left blank (e.g. on an asset
        // created before a field was added — Unity deserializes those with empty strings, so the
        // getters below fall back to these instead of sending an empty value).

        /// <summary>Global realtime streaming endpoint.</summary>
        public const string DefaultEndpoint = "wss://api.munsit.com/api/v1/listen";

        /// <summary>UAE data-residency endpoint. NOTE: keys are region-specific and not interchangeable.</summary>
        public const string UaeEndpoint = "wss://ae.api.faseeh.ai/api/v1/listen";

        /// <summary>Pure-Arabic model — fastest, best dialect coverage, supports hotwords.</summary>
        public const string DefaultModel = "munsit";

        /// <summary>Arabic/English code-switching model. NOTE: Munsit ignores hotwords on this model.</summary>
        public const string CodeSwitchingModel = "munsit-en-ar";

        // Domain vocabulary for the fish market: the six fish types (matching fishTypeArabic on the
        // Fish_*.asset ItemData in Assets/Runtime/Content/Fish/), weight/price terms, the spelled-out
        // Egyptian numerals the merchant prompt asks the player to use, and the accept/decline phrases
        // that close or continue a haggle.
        //
        // On the accept/decline block: these are everyday Arabic, which normally argues AGAINST
        // hotwording them. They are here anyway because they are the highest-consequence words in the
        // whole exchange — MerchantNegotiationLLM scores acceptance and rejection from the transcript,
        // so a "ماشي" that Munsit hears as something else does not merely read badly, it loses the sale.
        // Biasing them makes the transcript the scorer sees more faithful.
        //
        // NOTE: this is recognition biasing, NOT intent detection. Nothing keys off these strings —
        // the accept/decline decision stays with the LLM's scored judgment (see CLAUDE.md).
        public const string DefaultHotwords =
            // Fish types
            "بوري,دنيس,قاروص,سبيطي,وقار,بلاميدة," +
            // Weight, price and haggling terms
            "جنيه,كيلو,نص كيلو,ربع كيلو,السعر,بكام,تمن,غالي,رخيص,خصم,آخر كلام," +
            // Spelled-out Egyptian numerals
            "مية,ميتين,تلتمية,ربعمية,خمسمية,ألف," +
            "عشرة,عشرين,تلاتين,أربعين,خمسين,ستين,سبعين,تمانين,تسعين," +
            // Acceptance — closing the deal
            "ماشي,تمام,خلاص,اتفقنا,موافق,قبلت,طيب,حلو,أوكي,يلا,صفقة," +
            "خدها,هاتها,بعها,ماشي كده,تمام كده,على بركة الله," +
            // Rejection / pushback — keeping the haggle alive
            "لأ,مش,مينفعش,مش هينفع,مش موافق,مش راضي,مستحيل,ارفض,رافض," +
            "كتير,كتير أوي,غالي أوي,كده كتير,لسه غالي,حرام," +
            "بلاش,سيبك,معلش,نزّل,زوّد,زوّد شوية,نقّص,أقل,أكتر,آخر سعر";

        [Header("Authentication")]
        [Tooltip("Munsit API key, sent as the 'x-api-key' request header. Keys are region-specific: a " +
                 "global key will not authenticate against the UAE endpoint.")]
        [SerializeField] private string _apiKey;

        [Header("Endpoint")]
        [Tooltip("Realtime streaming WebSocket URL. Blank falls back to the global endpoint; switch to " +
                 "the UAE endpoint (and a UAE key) if data residency is required.")]
        [SerializeField] private string _endpoint = DefaultEndpoint;

        [Header("Model")]
        [Tooltip("'munsit' = pure Arabic, fastest, supports hotwords. 'munsit-en-ar' = Arabic/English " +
                 "code-switching, but hotwords are IGNORED on it. Blank falls back to 'munsit'.")]
        [SerializeField] private string _model = DefaultModel;

        [Tooltip("Comma-separated custom vocabulary biasing recognition toward in-domain terms (fish " +
                 "types, price/weight words, Egyptian numerals). Max 200 entries of 40 characters; " +
                 "over-long entries are dropped client-side with a warning. Blank = send none. " +
                 "Ignored entirely when the model is 'munsit-en-ar'.")]
        [SerializeField, TextArea(4, 10)] private string _hotwords = DefaultHotwords;

        [Header("Turn Detection")]
        [Tooltip("Semantic turn-completion model layered on top of silence detection — the server " +
                 "decides the player has finished a thought rather than waiting out the full silence " +
                 "window. Recommended ON for natural negotiation pacing.")]
        [SerializeField] private bool _smartTurn = true;

        [Tooltip("Stream revisable partial transcripts mid-utterance, for the live player caption. " +
                 "Display only — partials must never drive an accept/decline decision.")]
        [SerializeField] private bool _interimResults = true;

        [Tooltip("Milliseconds of silence that end a turn. 0 = derive it from the capture source's " +
                 "Silence Threshold Seconds (the VoiceCaptureController's Vad Silence Seconds), which " +
                 "is the recommended setting. Otherwise clamped to Munsit's 100-5000 ms range.")]
        [SerializeField, Min(0)] private int _endpointingMillisecondsOverride;

        /// <summary>Munsit API key sent as the 'x-api-key' header.</summary>
        public string ApiKey => _apiKey;

        /// <summary>Configured streaming endpoint, or <see cref="DefaultEndpoint"/> when left blank.</summary>
        public string Endpoint => string.IsNullOrWhiteSpace(_endpoint) ? DefaultEndpoint : _endpoint;

        /// <summary>Configured model ID, or <see cref="DefaultModel"/> when left blank.</summary>
        public string Model => string.IsNullOrWhiteSpace(_model) ? DefaultModel : _model;

        /// <summary>Raw comma-separated hotword list; may be empty (meaning: send none).</summary>
        public string Hotwords => _hotwords ?? string.Empty;

        /// <summary>True when <see cref="Model"/> is the code-switching model, which ignores hotwords.</summary>
        public bool ModelIgnoresHotwords => Model == CodeSwitchingModel;

        /// <summary>Enable the server's semantic turn-completion model.</summary>
        public bool SmartTurn => _smartTurn;

        /// <summary>Request revisable partial transcripts for the live caption.</summary>
        public bool InterimResults => _interimResults;

        /// <summary>Explicit endpointing in ms, or 0 to derive it from the capture silence threshold.</summary>
        public int EndpointingMillisecondsOverride => _endpointingMillisecondsOverride;
    }
}
