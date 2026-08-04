using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Owns the player's options and applies them. Registered Eager so saved volumes reach the
    /// mixer and the subtitle mode is published before the main menu — or any caption presenter —
    /// exists.
    ///
    /// Persistence is PlayerPrefs, not the save file, so options apply on the main menu before a
    /// save is loaded and survive a New Game.
    ///
    /// Because it publishes its boot state during construction, before any UI is alive, UI must
    /// PULL current values when it opens rather than waiting for an event.
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private readonly IEventService _eventService;
        private readonly ISoundMixerService _mixer;

        // AudioChannel is a contiguous 0..4 enum, so it indexes this directly.
        private readonly float[] _volumes = new float[5];

        // Two independent values, not one three-way enum. Language must always be a real language —
        // it draws every panel in the game — while captions are free to be off. Folding them
        // together meant turning captions off silently reset the interface to English.
        private GameLanguage _language;
        private bool _subtitlesOn;

        public GameLanguage Language => _language;
        public bool SubtitlesEnabled => _subtitlesOn;
        public GameLanguage SubtitleLanguage => _language;

        public SubtitleMode SubtitleMode =>
            !_subtitlesOn ? DumyatRuntime.SubtitleMode.Off
            : _language == GameLanguage.Arabic ? DumyatRuntime.SubtitleMode.Arabic
            : DumyatRuntime.SubtitleMode.English;

        public SettingsService(IEventService eventService, ISoundMixerService mixer)
        {
            _eventService = eventService;
            _mixer = mixer;

            if (_mixer == null)
                Debug.LogWarning($"{nameof(SettingsService)} has no {nameof(ISoundMixerService)} — " +
                                 "volume settings will be stored but never heard.");

            ReadFromPrefs();
            ApplyAll();
        }

        public float GetVolume(AudioChannel channel) => _volumes[(int)channel];

        public void SetVolume(AudioChannel channel, float value01, bool persistNow = true)
        {
            float clamped = Mathf.Clamp01(value01);
            _volumes[(int)channel] = clamped;

            // Straight to the mixer every call so a dragged slider is audible immediately.
            // Note SoundMixerManager.SetVolume also writes the pref, which is why the explicit
            // PlayerPrefs.Save below is the only thing gated on persistNow — the value is already
            // in the in-memory pref store either way, we are just controlling the disk write.
            _mixer?.SetVolume(channel, clamped);

            if (persistNow) PlayerPrefs.Save();

            _eventService?.Publish(new AudioVolumeChangedEvent(channel, clamped));
        }

        public void SetLanguage(GameLanguage language)
        {
            if (_language == language) return;

            _language = language;
            PlayerPrefs.SetInt(SettingsKeys.Language, (int)language);
            PlayerPrefs.Save();

            PublishSubtitleState();
        }

        public void SetSubtitlesEnabled(bool enabled)
        {
            if (_subtitlesOn == enabled) return;

            _subtitlesOn = enabled;
            PlayerPrefs.SetInt(SettingsKeys.SubtitlesOn, enabled ? 1 : 0);
            PlayerPrefs.Save();

            PublishSubtitleState();
        }

        /// <summary>
        /// Off turns captions off and leaves the language untouched — the old behaviour threw the
        /// language away, which is exactly what made "Arabic interface, no captions" impossible.
        /// </summary>
        public void SetSubtitleMode(SubtitleMode mode)
        {
            if (mode == DumyatRuntime.SubtitleMode.Off)
            {
                SetSubtitlesEnabled(false);
                return;
            }

            GameLanguage language = mode == DumyatRuntime.SubtitleMode.Arabic
                ? GameLanguage.Arabic
                : GameLanguage.English;

            // Both may change, and each setter early-outs on no-op, so publishing is left to
            // whichever actually moved.
            SetLanguage(language);
            SetSubtitlesEnabled(true);
        }

        public void ApplyAll()
        {
            if (_mixer != null)
            {
                for (int i = 0; i < _volumes.Length; i++)
                    _mixer.SetVolume((AudioChannel)i, _volumes[i]);
            }

            PublishSubtitleState();
        }

        public void ResetToDefaults()
        {
            for (int i = 0; i < _volumes.Length; i++)
            {
                _volumes[i] = SettingsKeys.DefaultVolume;
                _mixer?.SetVolume((AudioChannel)i, SettingsKeys.DefaultVolume);
            }

            _language = GameLanguage.English;
            _subtitlesOn = true;
            PlayerPrefs.SetInt(SettingsKeys.Language, (int)_language);
            PlayerPrefs.SetInt(SettingsKeys.SubtitlesOn, 1);
            PlayerPrefs.Save();

            PublishSubtitleState();
        }

        private void ReadFromPrefs()
        {
            for (int i = 0; i < _volumes.Length; i++)
            {
                _volumes[i] = Mathf.Clamp01(PlayerPrefs.GetFloat(
                    SettingsKeys.VolumeKey((AudioChannel)i), SettingsKeys.DefaultVolume));
            }

            ReadLanguageAndSubtitles();
        }

        /// <summary>
        /// Reads the two current keys, falling back to the legacy three-way
        /// <see cref="SettingsKeys.SubtitleMode"/> for a player who last played before the split.
        ///
        /// The migration keeps the only meaning the old value could carry: Off meant captions off,
        /// and it had no language to remember, so those players start in English with captions off
        /// and can now pick a language independently.
        /// </summary>
        private void ReadLanguageAndSubtitles()
        {
            bool hasNew = PlayerPrefs.HasKey(SettingsKeys.Language) ||
                          PlayerPrefs.HasKey(SettingsKeys.SubtitlesOn);

            if (hasNew)
            {
                int lang = PlayerPrefs.GetInt(SettingsKeys.Language, (int)GameLanguage.English);
                _language = lang == (int)GameLanguage.Arabic ? GameLanguage.Arabic : GameLanguage.English;
                _subtitlesOn = PlayerPrefs.GetInt(SettingsKeys.SubtitlesOn, 1) != 0;
                return;
            }

            int legacy = PlayerPrefs.GetInt(SettingsKeys.SubtitleMode, (int)DumyatRuntime.SubtitleMode.English);

            switch (legacy)
            {
                case (int)DumyatRuntime.SubtitleMode.Off:
                    _language = GameLanguage.English;
                    _subtitlesOn = false;
                    break;

                case (int)DumyatRuntime.SubtitleMode.Arabic:
                    _language = GameLanguage.Arabic;
                    _subtitlesOn = true;
                    break;

                default:
                    _language = GameLanguage.English;
                    _subtitlesOn = true;
                    break;
            }

            // Written through immediately so the legacy key is only ever read once.
            PlayerPrefs.SetInt(SettingsKeys.Language, (int)_language);
            PlayerPrefs.SetInt(SettingsKeys.SubtitlesOn, _subtitlesOn ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Announces the current state on every channel.
        ///
        /// <see cref="LanguageChangedEvent"/> is the one UI panels listen to — it fires whenever the
        /// language moves, including while captions are off, which a
        /// <see cref="SubtitleModeChangedEvent"/> alone would miss (the folded value stays Off in
        /// that case, so a language switch would look like no change at all).
        /// </summary>
        private void PublishSubtitleState()
        {
            if (_eventService == null) return;

            _eventService.Publish(new LanguageChangedEvent(_language));
            _eventService.Publish(new SubtitleModeChangedEvent(SubtitleMode));
            _eventService.Publish(new SubtitleSettingsChangedEvent(SubtitlesEnabled, 2));
        }
    }
}
