using UnityEngine;
using UnityEngine.Audio;

namespace DumyatRuntime
{
    public class SoundMixerManager : MonoBehaviour, ISoundMixerService
    {
        [SerializeField] private AudioMixer audioMixer;

        private const string MASTER_VOLUME_KEY = "MasterVolume";
        private const string MUSIC_VOLUME_KEY = "MusicVolume";
        private const string SOUND_FX_VOLUME_KEY = "SFXVolume";

        private void Awake()
        {
            LoadAndApplyVolumeSettings();
        }

        #region Public Setters (Called by UI Sliders or scripts)
        public void SetMasterVolume(float volume)
        {
            float clampedVolume = Mathf.Clamp(volume, 0.0001f, 1f);
            SetMixerVolume(MASTER_VOLUME_KEY, clampedVolume);
            AudioListener.volume = Mathf.Clamp01(volume);

            PlayerPrefs.SetFloat(MASTER_VOLUME_KEY, volume);
            PlayerPrefs.Save();
        }

        public void SetMusicVolume(float volume)
        {
            float clampedVolume = Mathf.Clamp(volume, 0.0001f, 1f);
            SetMixerVolume(MUSIC_VOLUME_KEY, clampedVolume);

            PlayerPrefs.SetFloat(MUSIC_VOLUME_KEY, volume);
            PlayerPrefs.Save();
        }

        public void SetSoundFXVolume(float volume)
        {
            float clampedVolume = Mathf.Clamp(volume, 0.0001f, 1f);
            SetMixerVolume(SOUND_FX_VOLUME_KEY, clampedVolume);

            PlayerPrefs.SetFloat(SOUND_FX_VOLUME_KEY, volume);
            PlayerPrefs.Save();
        }
        #endregion

        #region Loading Logic
        private void LoadAndApplyVolumeSettings()
        {

            float masterVol = PlayerPrefs.GetFloat(MASTER_VOLUME_KEY, 1f);
            float musicVol = PlayerPrefs.GetFloat(MUSIC_VOLUME_KEY, 1f);
            float sfxVol = PlayerPrefs.GetFloat(SOUND_FX_VOLUME_KEY, 1f);

            SetMixerVolume(MASTER_VOLUME_KEY, Mathf.Clamp(masterVol, 0.0001f, 1f));
            SetMixerVolume(MUSIC_VOLUME_KEY, Mathf.Clamp(musicVol, 0.0001f, 1f));
            SetMixerVolume(SOUND_FX_VOLUME_KEY, Mathf.Clamp(sfxVol, 0.0001f, 1f));
            AudioListener.volume = Mathf.Clamp01(masterVol);
        }
        #endregion

        private void SetMixerVolume(string exposedParameterName, float linearVolume)
        {
            if (audioMixer == null)
            {
                Debug.LogWarning("SoundMixerManager: AudioMixer is not assigned.");
                return;
            }

            bool applied = audioMixer.SetFloat(exposedParameterName, Mathf.Log10(linearVolume) * 20f);
            if (!applied)
            {
                Debug.LogWarning($"SoundMixerManager: AudioMixer parameter '{exposedParameterName}' is not exposed or does not exist.");
            }
        }

        #region Public Getters (For initializing UI Sliders)
        public float GetSavedMasterVolume() => PlayerPrefs.GetFloat(MASTER_VOLUME_KEY, 1f);
        public float GetSavedMusicVolume() => PlayerPrefs.GetFloat(MUSIC_VOLUME_KEY, 1f);
        public float GetSavedSoundFXVolume() => PlayerPrefs.GetFloat(SOUND_FX_VOLUME_KEY, 1f);
        #endregion
    }
}
