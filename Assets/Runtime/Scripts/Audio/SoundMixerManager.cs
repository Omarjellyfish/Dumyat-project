using UnityEngine;
using UnityEngine.Audio;

namespace DumyatRuntime
{
    public class SoundMixerManager : MonoBehaviour
    {
        [SerializeField] private AudioMixer audioMixer;

        private const string MasterVolumeKey = "Volume_Master";
        private const string MusicVolumeKey = "Volume_Music";
        private const string SoundFXVolumeKey = "Volume_SFX";

        private void Start()
        {
            
            LoadAndApplyVolumeSettings();
        }

        #region Public Setters (Called by UI Sliders or scripts)
        public void SetMasterVolume(float volume)
        {
            float clampedVolume = Mathf.Clamp(volume, 0.0001f, 1f);
            audioMixer.SetFloat("MasterVolume", Mathf.Log10(clampedVolume) * 20f);

            PlayerPrefs.SetFloat(MasterVolumeKey, volume);
            PlayerPrefs.Save();
        }

        public void SetMusicVolume(float volume)
        {
            float clampedVolume = Mathf.Clamp(volume, 0.0001f, 1f);
            audioMixer.SetFloat("MusicVolume", Mathf.Log10(clampedVolume) * 20f);

            PlayerPrefs.SetFloat(MusicVolumeKey, volume);
            PlayerPrefs.Save();
        }

        public void SetSoundFXVolume(float volume)
        {
            float clampedVolume = Mathf.Clamp(volume, 0.0001f, 1f);
            audioMixer.SetFloat("SoundFXVolume", Mathf.Log10(clampedVolume) * 20f);

            PlayerPrefs.SetFloat(SoundFXVolumeKey, volume);
            PlayerPrefs.Save();
        }
        #endregion

        #region Loading Logic
        private void LoadAndApplyVolumeSettings()
        {

            float masterVol = PlayerPrefs.GetFloat(MasterVolumeKey, 1f);
            float musicVol = PlayerPrefs.GetFloat(MusicVolumeKey, 1f);
            float sfxVol = PlayerPrefs.GetFloat(SoundFXVolumeKey, 1f);

            audioMixer.SetFloat("MasterVolume", Mathf.Log10(Mathf.Clamp(masterVol, 0.0001f, 1f)) * 20f);
            audioMixer.SetFloat("MusicVolume", Mathf.Log10(Mathf.Clamp(musicVol, 0.0001f, 1f)) * 20f);
            audioMixer.SetFloat("SoundFXVolume", Mathf.Log10(Mathf.Clamp(sfxVol, 0.0001f, 1f)) * 20f);
        }
        #endregion

        #region Public Getters (For initializing UI Sliders)
        public float GetSavedMasterVolume() => PlayerPrefs.GetFloat(MasterVolumeKey, 1f);
        public float GetSavedMusicVolume() => PlayerPrefs.GetFloat(MusicVolumeKey, 1f);
        public float GetSavedSoundFXVolume() => PlayerPrefs.GetFloat(SoundFXVolumeKey, 1f);
        #endregion
    }
}