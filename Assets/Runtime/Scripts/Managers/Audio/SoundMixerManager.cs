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

        private void Start()
        {

            LoadAndApplyVolumeSettings();
        }

        #region Public Setters (Called by UI Sliders or scripts)
        public void SetMasterVolume(float volume)
        {
            float clampedVolume = Mathf.Clamp(volume, 0.0001f, 1f);
            audioMixer.SetFloat(MASTER_VOLUME_KEY, Mathf.Log10(clampedVolume) * 20f);

            PlayerPrefs.SetFloat(MASTER_VOLUME_KEY, volume);
            PlayerPrefs.Save();
        }

        public void SetMusicVolume(float volume)
        {
            float clampedVolume = Mathf.Clamp(volume, 0.0001f, 1f);
            audioMixer.SetFloat(MUSIC_VOLUME_KEY, Mathf.Log10(clampedVolume) * 20f);

            PlayerPrefs.SetFloat(MUSIC_VOLUME_KEY, volume);
            PlayerPrefs.Save();
        }

        public void SetSoundFXVolume(float volume)
        {
            float clampedVolume = Mathf.Clamp(volume, 0.0001f, 1f);
            audioMixer.SetFloat(SOUND_FX_VOLUME_KEY, Mathf.Log10(clampedVolume) * 20f);

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

            audioMixer.SetFloat(MASTER_VOLUME_KEY, Mathf.Log10(Mathf.Clamp(masterVol, 0.0001f, 1f)) * 20f);
            audioMixer.SetFloat(MUSIC_VOLUME_KEY, Mathf.Log10(Mathf.Clamp(musicVol, 0.0001f, 1f)) * 20f);
            audioMixer.SetFloat(SOUND_FX_VOLUME_KEY, Mathf.Log10(Mathf.Clamp(sfxVol, 0.0001f, 1f)) * 20f);
        }
        #endregion

        #region Public Getters (For initializing UI Sliders)
        public float GetSavedMasterVolume() => PlayerPrefs.GetFloat(MASTER_VOLUME_KEY, 1f);
        public float GetSavedMusicVolume() => PlayerPrefs.GetFloat(MUSIC_VOLUME_KEY, 1f);
        public float GetSavedSoundFXVolume() => PlayerPrefs.GetFloat(SOUND_FX_VOLUME_KEY, 1f);
        #endregion
    }
}