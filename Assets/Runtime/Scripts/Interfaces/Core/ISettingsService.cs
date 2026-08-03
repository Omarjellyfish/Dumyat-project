namespace DumyatRuntime
{
    /// <summary>
    /// Player-facing options: audio levels and subtitle mode. Backed by
    /// <see cref="SettingsService"/>, which persists to PlayerPrefs rather than the save file so
    /// settings apply on the main menu, before any save has been loaded, and survive a New Game.
    /// </summary>
    public interface ISettingsService
    {
        SubtitleMode SubtitleMode { get; }

        /// <summary>Convenience for consumers that only care whether captions are showing at all.</summary>
        bool SubtitlesEnabled { get; }

        /// <summary>
        /// Which language to render captions in. Meaningless when <see cref="SubtitlesEnabled"/>
        /// is false — check that first.
        /// </summary>
        GameLanguage SubtitleLanguage { get; }

        /// <summary>Linear 0..1, as the sliders show it — not decibels.</summary>
        float GetVolume(AudioChannel channel);

        void SetSubtitleMode(SubtitleMode mode);

        /// <summary>
        /// Applies to the mixer immediately so the change is audible while a slider is dragged.
        /// Pass <paramref name="persistNow"/> false during the drag and true on release:
        /// PlayerPrefs.Save is a synchronous disk write and will hitch a headset at 90 Hz if it
        /// runs every frame.
        /// </summary>
        void SetVolume(AudioChannel channel, float value01, bool persistNow = true);

        /// <summary>Pushes every current value to the mixer and republishes the settings events.</summary>
        void ApplyAll();

        void ResetToDefaults();
    }
}
