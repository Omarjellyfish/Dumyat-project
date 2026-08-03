using UnityEngine;
using UnityEngine.UI;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public class SettingsUI : MonoBehaviour
    {
        private const string MASTER_VOLUME_KEY = "MasterVolume";
        private const string MUSIC_VOLUME_KEY = "MusicVolume";
        private const string SOUND_FX_VOLUME_KEY = "SFXVolume";

        [Header("Subtitles")]
        [SerializeField] private Toggle _subtitlesToggle;
        [SerializeField] private Slider _subtitleSizeSlider;
        [SerializeField] private Dropdown _languageDropdown;

        [Header("Audio")]
        [SerializeField] private Slider _masterVolumeSlider;
        [SerializeField] private Slider _voiceVolumeSlider;
        [SerializeField] private Slider _ambientVolumeSlider;

        private ISoundMixerService _soundMixerService;
        private ILegacySettingsService _settingsService;

        [Inject]
        private void Inject(ISoundMixerService soundMixerService, ILegacySettingsService settingsService)
        {
            _soundMixerService = soundMixerService;
            _settingsService = settingsService;
        }

        private void OnEnable()
        {
            InitializeUI();
            WarnIfAudioServicesMissing();

            if (_subtitlesToggle != null)
            {
                _subtitlesToggle.onValueChanged.RemoveListener(OnSubtitlesChanged);
                _subtitlesToggle.onValueChanged.AddListener(OnSubtitlesChanged);
            }
            if (_subtitleSizeSlider != null)
            {
                _subtitleSizeSlider.onValueChanged.RemoveListener(OnSubtitleSizeChanged);
                _subtitleSizeSlider.onValueChanged.AddListener(OnSubtitleSizeChanged);
            }
            if (_languageDropdown != null)
            {
                _languageDropdown.onValueChanged.RemoveListener(OnLanguageChanged);
                _languageDropdown.onValueChanged.AddListener(OnLanguageChanged);
            }
            if (_masterVolumeSlider != null)
            {
                _masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);
                _masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
            }
            if (_voiceVolumeSlider != null)
            {
                _voiceVolumeSlider.onValueChanged.RemoveListener(OnVoiceVolumeChanged);
                _voiceVolumeSlider.onValueChanged.AddListener(OnVoiceVolumeChanged);
            }
            if (_ambientVolumeSlider != null)
            {
                _ambientVolumeSlider.onValueChanged.RemoveListener(OnAmbientVolumeChanged);
                _ambientVolumeSlider.onValueChanged.AddListener(OnAmbientVolumeChanged);
            }
        }

        private void OnDisable()
        {
            if (_subtitlesToggle != null)
                _subtitlesToggle.onValueChanged.RemoveListener(OnSubtitlesChanged);
            if (_subtitleSizeSlider != null)
                _subtitleSizeSlider.onValueChanged.RemoveListener(OnSubtitleSizeChanged);
            if (_languageDropdown != null)
                _languageDropdown.onValueChanged.RemoveListener(OnLanguageChanged);
            if (_masterVolumeSlider != null)
                _masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);
            if (_voiceVolumeSlider != null)
                _voiceVolumeSlider.onValueChanged.RemoveListener(OnVoiceVolumeChanged);
            if (_ambientVolumeSlider != null)
                _ambientVolumeSlider.onValueChanged.RemoveListener(OnAmbientVolumeChanged);
        }

        private void InitializeUI()
        {
            float masterVolume = _settingsService != null ? _settingsService.MasterVolume : GetSavedMasterVolumeFallback();
            float voiceVolume = _settingsService != null ? _settingsService.MusicVolume : GetSavedMusicVolumeFallback();
            float ambientVolume = _settingsService != null ? _settingsService.SoundFXVolume : GetSavedSoundFXVolumeFallback();

            if (_masterVolumeSlider != null)
                _masterVolumeSlider.SetValueWithoutNotify(masterVolume);
            if (_voiceVolumeSlider != null)
                _voiceVolumeSlider.SetValueWithoutNotify(voiceVolume);
            if (_ambientVolumeSlider != null)
                _ambientVolumeSlider.SetValueWithoutNotify(ambientVolume);

            if (_subtitlesToggle != null && _settingsService != null)
                _subtitlesToggle.SetIsOnWithoutNotify(_settingsService.SubtitlesEnabled);
            if (_subtitleSizeSlider != null && _settingsService != null)
                _subtitleSizeSlider.SetValueWithoutNotify(_settingsService.SubtitleSize);
            if (_languageDropdown != null && _settingsService != null)
                _languageDropdown.SetValueWithoutNotify((int)_settingsService.Language);
        }

        private void OnSubtitlesChanged(bool value)
        {
            if (_settingsService == null) return;
            _settingsService.SetSubtitles(value);
        }

        private void OnSubtitleSizeChanged(float value)
        {
            if (_settingsService == null) return;
            _settingsService.SetSubtitleSize(Mathf.RoundToInt(value));
        }

        private void OnLanguageChanged(int value)
        {
            if (_settingsService == null) return;
            _settingsService.SetLanguage((GameLanguage)value);
        }

        private void OnMasterVolumeChanged(float value)
        {
            if (_settingsService != null) _settingsService.SetMasterVolume(value);
            else _soundMixerService?.SetMasterVolume(value);

            if (_settingsService == null && _soundMixerService == null)
                ApplyMasterVolumeFallback(value);
        }

        private void OnVoiceVolumeChanged(float value)
        {
            if (_settingsService != null) _settingsService.SetMusicVolume(value);
            else _soundMixerService?.SetMusicVolume(value);

            if (_settingsService == null && _soundMixerService == null)
                SaveVolumeFallback(MUSIC_VOLUME_KEY, value);
        }

        private void OnAmbientVolumeChanged(float value)
        {
            if (_settingsService != null) _settingsService.SetSoundFXVolume(value);
            else _soundMixerService?.SetSoundFXVolume(value);

            if (_settingsService == null && _soundMixerService == null)
                SaveVolumeFallback(SOUND_FX_VOLUME_KEY, value);
        }

        private float GetSavedMasterVolumeFallback()
        {
            return _soundMixerService != null ? _soundMixerService.GetSavedMasterVolume() : PlayerPrefs.GetFloat(MASTER_VOLUME_KEY, 1f);
        }

        private float GetSavedMusicVolumeFallback()
        {
            return _soundMixerService != null ? _soundMixerService.GetSavedMusicVolume() : PlayerPrefs.GetFloat(MUSIC_VOLUME_KEY, 1f);
        }

        private float GetSavedSoundFXVolumeFallback()
        {
            return _soundMixerService != null ? _soundMixerService.GetSavedSoundFXVolume() : PlayerPrefs.GetFloat(SOUND_FX_VOLUME_KEY, 1f);
        }

        private void ApplyMasterVolumeFallback(float value)
        {
            float volume = Mathf.Clamp01(value);
            AudioListener.volume = volume;
            SaveVolumeFallback(MASTER_VOLUME_KEY, volume);
            Debug.LogWarning("SettingsUI: Audio services were not injected, applied Master volume directly as fallback.");
        }

        private void SaveVolumeFallback(string key, float value)
        {
            PlayerPrefs.SetFloat(key, Mathf.Clamp01(value));
            PlayerPrefs.Save();
        }

        private void WarnIfAudioServicesMissing()
        {
            if (_settingsService == null && _soundMixerService == null)
            {
                Debug.LogWarning("SettingsUI: Settings/audio services are not injected. Check that this UI is under a Reflex scene scope. Master volume fallback is active.");
            }
        }
    }
}
