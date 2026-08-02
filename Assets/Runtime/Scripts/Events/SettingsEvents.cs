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

    /// <summary>Published whenever one of the audio mixer volume settings changes.</summary>
    public readonly struct VolumeSettingsChangedEvent : IGameEvent
    {
        public readonly float MasterVolume;
        public readonly float MusicVolume;
        public readonly float SoundFXVolume;

        public VolumeSettingsChangedEvent(float masterVolume, float musicVolume, float soundFXVolume)
        {
            MasterVolume = masterVolume;
            MusicVolume = musicVolume;
            SoundFXVolume = soundFXVolume;
        }
    }

    /// <summary>Published when the main settings panel is opened or closed.</summary>
    public readonly struct SettingsPanelToggledEvent : IGameEvent
    {
        public readonly bool IsOpen;

        public SettingsPanelToggledEvent(bool isOpen)
        {
            IsOpen = isOpen;
        }
    }
}
