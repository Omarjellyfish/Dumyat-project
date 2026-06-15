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

        private void Start()
        {
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
            SettingsManager.Instance.SetSubtitles(_value);
        }

        private void OnMasterVolumeChanged(float _value)
        {
            SettingsManager.Instance.SetMasterVolume(_value);
        }

        private void OnVoiceVolumeChanged(float _value)
        {
            SettingsManager.Instance.SetVoiceVolume(_value);
        }

        private void OnAmbientVolumeChanged(float _value)
        {
            SettingsManager.Instance.SetAmbientVolume(_value);
        }
    }
}