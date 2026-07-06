using UnityEngine;
using UnityEngine.UI;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public class SettingsUI : MonoBehaviour
    {
        [Header("Subtitles")]
        [SerializeField] private Toggle _subtitlesToggle;

        [Header("Audio")]
        [SerializeField] private Slider _masterVolumeSlider;
        [SerializeField] private Slider _voiceVolumeSlider;
        [SerializeField] private Slider _ambientVolumeSlider;
        [SerializeField] private SettingsManager _settingsManager;

        private ISoundMixerService _soundMixerService;

        [Inject]
        private void Inject(ISoundMixerService soundMixerService) => _soundMixerService = soundMixerService;

        private void Start()
        {
            _masterVolumeSlider.value = _soundMixerService.GetSavedMasterVolume();
            _voiceVolumeSlider.value = _soundMixerService.GetSavedMusicVolume();
            _ambientVolumeSlider.value = _soundMixerService.GetSavedSoundFXVolume();

            _subtitlesToggle.onValueChanged.AddListener(OnSubtitlesChanged);
            _masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
            _voiceVolumeSlider.onValueChanged.AddListener(OnVoiceVolumeChanged);
            _ambientVolumeSlider.onValueChanged.AddListener(OnAmbientVolumeChanged);
        }

        private void OnDisable()
        {
            _subtitlesToggle.onValueChanged.RemoveAllListeners();
            _masterVolumeSlider.onValueChanged.RemoveAllListeners();
            _voiceVolumeSlider.onValueChanged.RemoveAllListeners();
            _ambientVolumeSlider.onValueChanged.RemoveAllListeners();
        }

        private void OnSubtitlesChanged(bool _value)
        {
            _settingsManager.SetSubtitles(_value);
        }

        private void OnMasterVolumeChanged(float _value)
        {
            _soundMixerService.SetMasterVolume(_value);
        }

        private void OnVoiceVolumeChanged(float _value)
        {
            _soundMixerService.SetMusicVolume(_value);

        }

        private void OnAmbientVolumeChanged(float _value)
        {
            _soundMixerService.SetSoundFXVolume(_value);
        }
    }
}