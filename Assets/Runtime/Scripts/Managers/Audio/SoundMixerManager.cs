using UnityEngine;
using UnityEngine.Audio;

namespace DumyatRuntime
{
    /// <summary>
    /// Converts linear 0..1 volumes into mixer decibels and persists them to PlayerPrefs.
    ///
    /// Note there are deliberately TWO writers of these prefs: this class (kept for the legacy
    /// settings UI) and <see cref="SettingsService"/> (the new menu). They agree because
    /// <see cref="SettingsKeys"/> mirrors the same key strings and the same 1f default, so the
    /// re-apply in <see cref="Start"/> writes identical values. See SettingsKeys for why that
    /// mirroring must be maintained.
    /// </summary>
    public class SoundMixerManager : MonoBehaviour, ISoundMixerService
    {
        [SerializeField] private AudioMixer audioMixer;

        private void Start()
        {
            LoadAndApplyVolumeSettings();
        }

        #region Public Setters (Called by UI Sliders or scripts)
        public void SetMasterVolume(float volume) => SetVolume(AudioChannel.Master, volume);
        public void SetMusicVolume(float volume) => SetVolume(AudioChannel.Music, volume);
        public void SetSoundFXVolume(float volume) => SetVolume(AudioChannel.SoundFX, volume);
        public void SetAmbienceVolume(float volume) => SetVolume(AudioChannel.Ambience, volume);
        public void SetDialogueVolume(float volume) => SetVolume(AudioChannel.Dialogue, volume);

        public void SetVolume(AudioChannel channel, float volume)
        {
            string key = SettingsKeys.VolumeKey(channel);

            ApplyToMixer(key, volume);

            PlayerPrefs.SetFloat(key, volume);
            PlayerPrefs.Save();
        }
        #endregion

        #region Loading Logic
        private void LoadAndApplyVolumeSettings()
        {
            ApplyToMixer(SettingsKeys.MasterVolume, GetSavedVolume(AudioChannel.Master));
            ApplyToMixer(SettingsKeys.MusicVolume, GetSavedVolume(AudioChannel.Music));
            ApplyToMixer(SettingsKeys.SfxVolume, GetSavedVolume(AudioChannel.SoundFX));
            ApplyToMixer(SettingsKeys.AmbienceVolume, GetSavedVolume(AudioChannel.Ambience));
            ApplyToMixer(SettingsKeys.DialogueVolume, GetSavedVolume(AudioChannel.Dialogue));
        }

        /// <summary>
        /// The one place a linear slider value becomes decibels. The 0.0001 floor exists because
        /// Log10(0) is negative infinity, which the mixer rejects.
        /// </summary>
        private void ApplyToMixer(string exposedParameter, float volume)
        {
            if (audioMixer == null)
            {
                Debug.LogWarning($"{nameof(SoundMixerManager)} on '{name}' has no AudioMixer assigned — " +
                                 "volume settings will have no effect.", this);
                return;
            }

            float clampedVolume = Mathf.Clamp(volume, 0.0001f, 1f);
            audioMixer.SetFloat(exposedParameter, Mathf.Log10(clampedVolume) * 20f);
        }
        #endregion

        #region Public Getters (For initializing UI Sliders)
        public float GetSavedMasterVolume() => GetSavedVolume(AudioChannel.Master);
        public float GetSavedMusicVolume() => GetSavedVolume(AudioChannel.Music);
        public float GetSavedSoundFXVolume() => GetSavedVolume(AudioChannel.SoundFX);
        public float GetSavedAmbienceVolume() => GetSavedVolume(AudioChannel.Ambience);
        public float GetSavedDialogueVolume() => GetSavedVolume(AudioChannel.Dialogue);

        public float GetSavedVolume(AudioChannel channel)
            => PlayerPrefs.GetFloat(SettingsKeys.VolumeKey(channel), SettingsKeys.DefaultVolume);
        #endregion
    }
}
