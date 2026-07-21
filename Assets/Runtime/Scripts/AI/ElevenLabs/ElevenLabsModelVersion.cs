namespace DumyatRuntime
{
    /// <summary>
    /// Which generation of the ElevenLabs text-to-speech model a voice line uses. The two versions
    /// take DIFFERENT voice parameters, so this drives both the model ID and which parameter set
    /// <see cref="ElevenLabsClient"/> serialises into the request:
    /// <list type="bullet">
    /// <item><see cref="V2"/> — eleven_multilingual_v2 / flash_v2_5: speed, stability, similarity, style.</item>
    /// <item><see cref="V3"/> — eleven_v3: stability + language override only (more expressive).</item>
    /// </list>
    /// </summary>
    public enum ElevenLabsModelVersion
    {
        /// <summary>Eleven v2 family (multilingual_v2 / flash_v2_5). Takes speed, stability, similarity, style.</summary>
        V2 = 0,

        /// <summary>Eleven v3. Takes stability + language override only.</summary>
        V3 = 1,
    }
}
