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
        private SubtitleMode _subtitleMode;

        public SubtitleMode SubtitleMode => _subtitleMode;
        public bool SubtitlesEnabled => _subtitleMode != DumyatRuntime.SubtitleMode.Off;
        public GameLanguage SubtitleLanguage =>
            _subtitleMode == DumyatRuntime.SubtitleMode.Arabic ? GameLanguage.Arabic : GameLanguage.English;

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

        public void SetSubtitleMode(SubtitleMode mode)
        {
            if (_subtitleMode == mode) return;

            _subtitleMode = mode;
            PlayerPrefs.SetInt(SettingsKeys.SubtitleMode, (int)mode);
            PlayerPrefs.Save();

            PublishSubtitleState();
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

            _subtitleMode = DumyatRuntime.SubtitleMode.English;
            PlayerPrefs.SetInt(SettingsKeys.SubtitleMode, (int)_subtitleMode);
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

            int stored = PlayerPrefs.GetInt(SettingsKeys.SubtitleMode, (int)DumyatRuntime.SubtitleMode.English);
            _subtitleMode = stored >= (int)DumyatRuntime.SubtitleMode.Off &&
                            stored <= (int)DumyatRuntime.SubtitleMode.Arabic
                ? (SubtitleMode)stored
                : DumyatRuntime.SubtitleMode.English;
        }

        /// <summary>
        /// Publishes the new single-enum event plus the two older ones. The legacy contracts had no
        /// publisher until now; emitting all three means anything written against either shape works.
        /// </summary>
        private void PublishSubtitleState()
        {
            if (_eventService == null) return;

            _eventService.Publish(new SubtitleModeChangedEvent(_subtitleMode));
            _eventService.Publish(new SubtitleSettingsChangedEvent(SubtitlesEnabled, 2));
            _eventService.Publish(new LanguageChangedEvent(SubtitleLanguage));
        }
    }
}
