namespace DumyatRuntime
{
    public enum GameLanguage
    {
        English,
        Arabic
    }

    /// <summary>
    /// Published by SettingsManager whenever subtitle settings change.
    /// (Was referenced in SettingsManager.cs but never actually defined - added here.)
    /// </summary>
    public readonly struct SubtitleSettingsChangedEvent : IGameEvent
    {
        public readonly bool SubtitlesEnabled;
        public readonly int SubtitleSize;

        public SubtitleSettingsChangedEvent(bool subtitlesEnabled, int subtitleSize)
        {
            SubtitlesEnabled = subtitlesEnabled;
            SubtitleSize = subtitleSize;
        }
    }

    /// <summary>Published by SettingsManager whenever the active language changes.</summary>
    public readonly struct LanguageChangedEvent : IGameEvent
    {
        public readonly GameLanguage Language;

        public LanguageChangedEvent(GameLanguage language)
        {
            Language = language;
        }
    }
}
