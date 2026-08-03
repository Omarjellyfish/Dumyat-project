using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public interface ISettingsService
    {
        bool SubtitlesEnabled { get; }
        int SubtitleSize { get; }
        GameLanguage Language { get; }
        bool IsArabic { get; }
        float MasterVolume { get; }
        float MusicVolume { get; }
        float SoundFXVolume { get; }

        void SetSubtitles(bool value);
        void SetSubtitleSize(int value);
        void SetLanguage(GameLanguage value);
        void SetMasterVolume(float value);
        void SetMusicVolume(float value);
        void SetSoundFXVolume(float value);
        void PublishCurrentSettings();
    }

    public class SettingsManager : MonoBehaviour, ISettingsService
    {
        private const string SUBTITLES_ENABLED_KEY = "SubtitlesEnabled";
        private const string SUBTITLE_SIZE_KEY = "SubtitleSize";
        private const string LANGUAGE_KEY = "Language";

        private bool _subtitlesEnabled = true;
        private int _subtitleSize = 2;
        private GameLanguage _language = GameLanguage.English;
        private float _masterVolume = 1f;
        private float _musicVolume = 1f;
        private float _soundFXVolume = 1f;

        private IEventService _eventService;
        private ISoundMixerService _soundMixerService;

        [Inject]
        private void Inject(IEventService eventService, ISoundMixerService soundMixerService)
        {
            _eventService = eventService;
            _soundMixerService = soundMixerService;
        }

        private void Awake()
        {
            LoadSavedSettings();
        }

        private void Start()
        {
            ApplySavedAudioSettings();
            PublishCurrentSettings();
        }

        public void SetSubtitles(bool value)
        {
            _subtitlesEnabled = value;
            PlayerPrefs.SetInt(SUBTITLES_ENABLED_KEY, _subtitlesEnabled ? 1 : 0);
            PlayerPrefs.Save();
            PublishSubtitleSettings();
        }

        public void SetSubtitleSize(int value)
        {
            _subtitleSize = value;
            PlayerPrefs.SetInt(SUBTITLE_SIZE_KEY, _subtitleSize);
            PlayerPrefs.Save();
            PublishSubtitleSettings();
        }

        public void SetLanguage(GameLanguage value)
        {
            _language = value;
            PlayerPrefs.SetInt(LANGUAGE_KEY, (int)_language);
            PlayerPrefs.Save();
            _eventService?.Publish(new LanguageChangedEvent(_language));
        }

        public void SetMasterVolume(float value)
        {
            _masterVolume = Mathf.Clamp01(value);
            _soundMixerService?.SetMasterVolume(_masterVolume);
            PublishVolumeSettings();
        }

        public void SetMusicVolume(float value)
        {
            _musicVolume = Mathf.Clamp01(value);
            _soundMixerService?.SetMusicVolume(_musicVolume);
            PublishVolumeSettings();
        }

        public void SetSoundFXVolume(float value)
        {
            _soundFXVolume = Mathf.Clamp01(value);
            _soundMixerService?.SetSoundFXVolume(_soundFXVolume);
            PublishVolumeSettings();
        }

        public void PublishCurrentSettings()
        {
            PublishSubtitleSettings();
            _eventService?.Publish(new LanguageChangedEvent(_language));
            PublishVolumeSettings();
        }

        private void LoadSavedSettings()
        {
            _subtitlesEnabled = PlayerPrefs.GetInt(SUBTITLES_ENABLED_KEY, 1) == 1;
            _subtitleSize = PlayerPrefs.GetInt(SUBTITLE_SIZE_KEY, 2);
            _language = (GameLanguage)PlayerPrefs.GetInt(LANGUAGE_KEY, (int)GameLanguage.English);

            _masterVolume = _soundMixerService != null
                ? _soundMixerService.GetSavedMasterVolume()
                : PlayerPrefs.GetFloat("MasterVolume", 1f);
            _musicVolume = _soundMixerService != null
                ? _soundMixerService.GetSavedMusicVolume()
                : PlayerPrefs.GetFloat("MusicVolume", 1f);
            _soundFXVolume = _soundMixerService != null
                ? _soundMixerService.GetSavedSoundFXVolume()
                : PlayerPrefs.GetFloat("SFXVolume", 1f);
        }

        private void ApplySavedAudioSettings()
        {
            _soundMixerService?.SetMasterVolume(_masterVolume);
            _soundMixerService?.SetMusicVolume(_musicVolume);
            _soundMixerService?.SetSoundFXVolume(_soundFXVolume);
        }

        private void PublishSubtitleSettings()
        {
            _eventService?.Publish(new SubtitleSettingsChangedEvent(_subtitlesEnabled, _subtitleSize));
        }

        private void PublishVolumeSettings()
        {
            _eventService?.Publish(new VolumeSettingsChangedEvent(_masterVolume, _musicVolume, _soundFXVolume));
        }

        public bool SubtitlesEnabled => _subtitlesEnabled;
        public int SubtitleSize => _subtitleSize;
        public GameLanguage Language => _language;
        public bool IsArabic => _language == GameLanguage.Arabic;
        public float MasterVolume => _masterVolume;
        public float MusicVolume => _musicVolume;
        public float SoundFXVolume => _soundFXVolume;
    }
}
