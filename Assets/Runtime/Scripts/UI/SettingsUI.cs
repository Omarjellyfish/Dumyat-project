using UnityEngine;
using UnityEngine.UI;

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
        [SerializeField] private SoundMixerManager soundManager;
        [SerializeField] private SettingsManager _settingsManager;

        private void Start()
        {
            _masterVolumeSlider.value = soundManager.GetSavedMasterVolume();
            _voiceVolumeSlider.value = soundManager.GetSavedMusicVolume();
            _ambientVolumeSlider.value = soundManager.GetSavedSoundFXVolume();

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
            soundManager.SetMasterVolume(_value);
        }

        private void OnVoiceVolumeChanged(float _value)
        {
            soundManager.SetMusicVolume(_value);

        }

        private void OnAmbientVolumeChanged(float _value)
        {
            soundManager.SetSoundFXVolume(_value);
        }
    }
}