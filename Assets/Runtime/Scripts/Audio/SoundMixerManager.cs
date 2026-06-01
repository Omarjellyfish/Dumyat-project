using UnityEngine;
using UnityEngine.Audio;

namespace DumyatRuntime
{
    public class SoundMixerManager : MonoBehaviour
    {
        [SerializeField] private AudioMixer audioMixer;

        // Using constant strings prevents typos across the script
        private const string MasterVolumeKey = "Volume_Master";
        private const string MusicVolumeKey = "Volume_Music";
        private const string SoundFXVolumeKey = "Volume_SFX";

        private void Start()
        {
            // Unity Mixer parameters should ideally be initialized in Start(), 
            // as the AudioMixer asset may not be fully loaded/accessible during Awake().
            LoadAndApplyVolumeSettings();
        }

        #region Public Setters (Called by UI Sliders or scripts)
        public void SetMasterVolume(float volume)
        {
            float clampedVolume = Mathf.Clamp(volume, 0.0001f, 1f);
            audioMixer.SetFloat("MasterVolume", Mathf.Log10(clampedVolume) * 20f);

            // Save the raw 0-1 slider value
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
            // GetFloat accepts a second parameter as a default value if the key doesn't exist yet.
            // We default to 1f (maximum volume) so players have sound on their very first boot.
            float masterVol = PlayerPrefs.GetFloat(MasterVolumeKey, 1f);
            float musicVol = PlayerPrefs.GetFloat(MusicVolumeKey, 1f);
            float sfxVol = PlayerPrefs.GetFloat(SoundFXVolumeKey, 1f);

            // Directly calculate and pass the dB value to the Mixer
            audioMixer.SetFloat("MasterVolume", Mathf.Log10(Mathf.Clamp(masterVol, 0.0001f, 1f)) * 20f);
            audioMixer.SetFloat("MusicVolume", Mathf.Log10(Mathf.Clamp(musicVol, 0.0001f, 1f)) * 20f);
            audioMixer.SetFloat("SoundFXVolume", Mathf.Log10(Mathf.Clamp(sfxVol, 0.0001f, 1f)) * 20f);
        }
        #endregion

        #region Public Getters (For initializing UI Sliders)
        // When your Settings Menu opens, your UI script can call these to position the sliders correctly.
        public float GetSavedMasterVolume() => PlayerPrefs.GetFloat(MasterVolumeKey, 1f);
        public float GetSavedMusicVolume() => PlayerPrefs.GetFloat(MusicVolumeKey, 1f);
        public float GetSavedSoundFXVolume() => PlayerPrefs.GetFloat(SoundFXVolumeKey, 1f);
        #endregion
    }
}