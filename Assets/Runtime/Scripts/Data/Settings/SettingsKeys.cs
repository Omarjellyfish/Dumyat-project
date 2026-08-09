namespace DumyatRuntime
{
    /// <summary>
    /// Every PlayerPrefs key and AudioMixer exposed-parameter name used by the settings system.
    ///
    /// The five volume strings are deliberately MIRRORED from the private consts in
    /// <see cref="SoundMixerManager"/>, not shared with it — that class is part of the legacy menu
    /// and is left untouched. Two owners write the same prefs, which is harmless only while these
    /// literals and the defaults in <see cref="SettingsService"/> match it byte for byte. If they
    /// drift, volumes silently revert one frame after boot when SoundMixerManager.Start() re-reads
    /// them. Change one, change both.
    /// </summary>
    public static class SettingsKeys
    {
        // --- Audio. Key name == exposed mixer parameter name, which is why the typo survives. ---
        public const string MasterVolume = "MasterVolume";
        public const string MusicVolume = "MusicVolume";
        public const string SfxVolume = "SFXVolume";
        public const string AmbienceVolume = "AmbienceVolume";

        /// <summary>
        /// Misspelled on purpose: "DialougeVolume" is the exposed parameter name authored into
        /// MainMixer.mixer. Correcting the spelling here breaks the mixer lookup silently —
        /// <c>AudioMixer.SetFloat</c> returns false rather than throwing.
        /// </summary>
        public const string DialogueVolume = "DialougeVolume";

        // --- Language & subtitles. Namespaced because, unlike the volumes, nothing legacy reads them. ---

        /// <summary>
        /// Which language the whole interface draws in. Stored as <see cref="GameLanguage"/>.
        /// </summary>
        public const string Language = "Dumyat.Language";

        /// <summary>Whether merchant captions are shown at all. Stored as 0/1.</summary>
        public const string SubtitlesOn = "Dumyat.SubtitlesOn";

        /// <summary>
        /// LEGACY. The old single three-way <see cref="DumyatRuntime.SubtitleMode"/> that folded
        /// language and on/off together. Kept only so <see cref="SettingsService"/> can migrate a
        /// player who already has one saved — nothing writes it any more.
        ///
        /// It was replaced because it could not express "Arabic interface, no merchant captions":
        /// choosing Off threw the language away, and once the setting drove the whole UI rather than
        /// just captions, that made picking Off silently revert the interface to English.
        /// </summary>
        public const string SubtitleMode = "Dumyat.SubtitleMode";

        // --- Microphone. Namespaced like the language keys; nothing legacy reads them. ---

        /// <summary>When the mic listens. Stored as <see cref="MicCaptureMode"/>.</summary>
        public const string MicCaptureMode = "Dumyat.MicCaptureMode";

        /// <summary>Adaptive noise-floor gate on/off. Stored as 0/1.</summary>
        public const string MicAutoSensitivity = "Dumyat.MicAutoSensitivity";

        /// <summary>
        /// Manual voice gate, in the same peak-amplitude domain the capture source compares against —
        /// NOT the 0-100 the slider shows. Only consulted while
        /// <see cref="MicAutoSensitivity"/> is off.
        /// </summary>
        public const string MicThreshold = "Dumyat.MicThreshold";

        /// <summary>Which controller button talks. Stored as <see cref="DumyatRuntime.PushToTalkButton"/>.</summary>
        public const string PushToTalkButton = "Dumyat.PushToTalkButton";

        /// <summary>Default for every audio channel, matching SoundMixerManager's PlayerPrefs fallback.</summary>
        public const float DefaultVolume = 1f;

        /// <summary>
        /// Default manual voice gate. Mirrors <c>VoiceCaptureController._vadAmplitudeThreshold</c>, so
        /// a player who never opens the Voice tab gets exactly the tuning the Inspector shipped with.
        /// </summary>
        public const float DefaultMicThreshold = 0.02f;

        /// <summary>
        /// Top of the manual gate range, mirroring the <c>[Range(0f, 0.5f)]</c> on that same field.
        /// This is what the 0-100 shown beside the slider is a percentage OF.
        /// </summary>
        public const float MaxMicThreshold = 0.5f;

        /// <summary>PlayerPrefs key (and mixer parameter name) for a channel.</summary>
        public static string VolumeKey(AudioChannel channel)
        {
            switch (channel)
            {
                case AudioChannel.Master: return MasterVolume;
                case AudioChannel.Music: return MusicVolume;
                case AudioChannel.SoundFX: return SfxVolume;
                case AudioChannel.Ambience: return AmbienceVolume;
                case AudioChannel.Dialogue: return DialogueVolume;
                default: return MasterVolume;
            }
        }
    }
}
