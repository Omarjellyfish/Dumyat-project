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

    /// <summary>
    /// Published by <see cref="SettingsService"/> when the subtitle mode changes. Prefer this over
    /// the two events above: it carries off/English/Arabic as one value that cannot self-contradict.
    /// The older pair is still published alongside it for anything already written against them.
    /// </summary>
    public readonly struct SubtitleModeChangedEvent : IGameEvent
    {
        public readonly SubtitleMode Mode;

        public SubtitleModeChangedEvent(SubtitleMode mode)
        {
            Mode = mode;
        }
    }

    /// <summary>Published by <see cref="SettingsService"/> on every volume change. Linear 0..1.</summary>
    public readonly struct AudioVolumeChangedEvent : IGameEvent
    {
        public readonly AudioChannel Channel;
        public readonly float Value01;

        public AudioVolumeChangedEvent(AudioChannel channel, float value01)
        {
            Channel = channel;
            Value01 = value01;
        }
    }
}
