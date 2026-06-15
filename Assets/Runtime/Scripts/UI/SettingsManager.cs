using UnityEngine;

namespace DumyatRuntime
{
    public class SettingsManager : MonoBehaviour
    {
        public static SettingsManager Instance { get; private set; }

        [Header("Subtitles")]
        private bool _subtitlesEnabled = true;

        [Header("Audio")]
        private float _masterVolume = 1f;
        private float _voiceVolume = 1f;
        private float _ambientVolume = 0.7f;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void SetSubtitles(bool _value) => _subtitlesEnabled = _value;
        public void SetMasterVolume(float _value) => _masterVolume = _value;
        public void SetVoiceVolume(float _value) => _voiceVolume = _value;
        public void SetAmbientVolume(float _value) => _ambientVolume = _value;

        public bool SubtitlesEnabled => _subtitlesEnabled;
        public float MasterVolume => _masterVolume;
        public float VoiceVolume => _voiceVolume;
        public float AmbientVolume => _ambientVolume;
    }
}