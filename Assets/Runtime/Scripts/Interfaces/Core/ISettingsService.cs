namespace DumyatRuntime
{
    /// <summary>
    /// Player-facing options: audio levels and subtitle mode. Backed by
    /// <see cref="SettingsService"/>, which persists to PlayerPrefs rather than the save file so
    /// settings apply on the main menu, before any save has been loaded, and survive a New Game.
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// The language the whole interface draws in — menus, the tutorial card, the departure
        /// board, the crafting sheets — not just captions. Always a real language: there is no
        /// "off", because a panel with no language has nothing to draw.
        /// </summary>
        GameLanguage Language { get; }

        /// <summary>Whether merchant captions are shown. Independent of <see cref="Language"/>.</summary>
        bool SubtitlesEnabled { get; }

        /// <summary>
        /// The two settings above folded back into the old three-way value, for anything written
        /// against it (notably <see cref="MerchantSubtitlePresenter"/>). Derived, never stored.
        /// </summary>
        SubtitleMode SubtitleMode { get; }

        /// <summary>
        /// Alias of <see cref="Language"/>. Kept because callers already read it; new code should
        /// prefer <see cref="Language"/>, which says what it actually controls.
        /// </summary>
        GameLanguage SubtitleLanguage { get; }

        /// <summary>Linear 0..1, as the sliders show it — not decibels.</summary>
        float GetVolume(AudioChannel channel);

        /// <summary>Switches the interface language. Does not touch whether captions are on.</summary>
        void SetLanguage(GameLanguage language);

        /// <summary>Turns merchant captions on or off. Does not touch the interface language.</summary>
        void SetSubtitlesEnabled(bool enabled);

        /// <summary>
        /// Sets both at once from the old combined value: <see cref="DumyatRuntime.SubtitleMode.Off"/>
        /// turns captions off and LEAVES the language alone, which is the whole reason the two were
        /// split apart.
        /// </summary>
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
