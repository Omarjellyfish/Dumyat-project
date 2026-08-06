namespace DumyatRuntime
{
    /// <summary>
    /// A per-speaker override of the ElevenLabs voice-synthesis parameters (see
    /// <see cref="ElevenLabsClient"/>). Passed down the TTS chain
    /// (<see cref="ITTSService"/> → <see cref="ITTSModel"/> → <see cref="ElevenLabsClient"/>) so each
    /// merchant can sound distinct while still sharing the pooled synthesis path. When a call supplies
    /// no settings (i.e. a <c>TtsVoiceSettings?</c> of <c>null</c>), the client falls back to its own
    /// serialized fields, so existing non-merchant callers are unaffected.
    ///
    /// The <see cref="Version"/> selects which parameters actually reach the request: Eleven v2 uses
    /// speed/stability/similarity/style; Eleven v3 uses stability + language override only. Build one
    /// via <see cref="ForV2"/> or <see cref="ForV3"/> so only the fields that version reads are set.
    /// Values map onto the ElevenLabs <c>voice_settings</c> block; the client still clamps them to the
    /// API's accepted ranges, so an out-of-range authored value can't kill a request.
    /// </summary>
    public readonly struct TtsVoiceSettings
    {
        /// <summary>Which ElevenLabs model generation these settings target (drives the model + param set).</summary>
        public readonly ElevenLabsModelVersion Version;

        /// <summary>[v2] Speaking speed. 1 = normal, &lt; 1 slower, &gt; 1 faster (ElevenLabs accepts 0.7–1.2).</summary>
        public readonly float Speed;

        /// <summary>[v2 + v3] Stability. Lower = more variable/expressive, higher = more stable/consistent.</summary>
        public readonly float Stability;

        /// <summary>[v2] Similarity boost. How closely the output matches the original voice.</summary>
        public readonly float SimilarityBoost;

        /// <summary>[v2] Style exaggeration. 0 = neutral, 1 = highly expressive.</summary>
        public readonly float Style;

        /// <summary>
        /// [v3] Language override (ISO 639-1, e.g. "ar"). <c>null</c> means "inherit the client's own
        /// language setting"; an empty string means "auto-detect" (omit the field from the request).
        /// v2 settings leave this <c>null</c> so the client's global language field still applies.
        /// </summary>
        public readonly string LanguageOverride;

        private TtsVoiceSettings(ElevenLabsModelVersion version, float speed, float stability,
            float similarityBoost, float style, string languageOverride)
        {
            Version = version;
            Speed = speed;
            Stability = stability;
            SimilarityBoost = similarityBoost;
            Style = style;
            LanguageOverride = languageOverride;
        }

        /// <summary>Settings for an Eleven v2 line (speed, stability, similarity, style).</summary>
        public static TtsVoiceSettings ForV2(float speed, float stability, float similarityBoost, float style) =>
            new TtsVoiceSettings(ElevenLabsModelVersion.V2, speed, stability, similarityBoost, style, null);

        /// <summary>
        /// Settings for an Eleven v3 line (stability + language override only). The v2-only fields are
        /// left at neutral values since v3 ignores them.
        /// </summary>
        public static TtsVoiceSettings ForV3(float stability, string languageOverride) =>
            new TtsVoiceSettings(ElevenLabsModelVersion.V3, 1f, stability, 0.75f, 0f,
                languageOverride ?? string.Empty);
    }
}
