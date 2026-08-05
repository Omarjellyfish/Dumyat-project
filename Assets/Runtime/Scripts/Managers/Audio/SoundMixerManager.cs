using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace DumyatRuntime
{
    /// <summary>
    /// Converts linear 0..1 volumes into mixer decibels and persists them to PlayerPrefs.
    ///
    /// Note there are deliberately TWO writers of these prefs: this class (kept for the legacy
    /// settings UI) and <see cref="SettingsService"/> (the new menu). They agree because
    /// <see cref="SettingsKeys"/> mirrors the same key strings and the same 1f default, so the
    /// re-apply below writes identical values. See SettingsKeys for why that mirroring must be
    /// maintained.
    /// </summary>
    public class SoundMixerManager : MonoBehaviour, ISoundMixerService
    {
        [SerializeField] private AudioMixer audioMixer;

        private const string MASTER_VOLUME_KEY = "MasterVolume";
        private const string MUSIC_VOLUME_KEY = "MusicVolume";
        private const string SOUND_FX_VOLUME_KEY = "SFXVolume";

        // Applied THREE times, and all three are load-bearing:
        //
        //  Awake  — so anything reading the mixer before the first frame sees the saved levels.
        //  Start  — because an Awake-time SetFloat does not survive. The mixer applies its own start
        //           SNAPSHOT as the audio system comes up, which overwrites every exposed parameter
        //           set during Awake. That is invisible for Master/Music/SFX/Dialogue, whose snapshot
        //           values are 0 dB and so happen to match a saved volume of 1.0 — but MainMixer
        //           snapshots Ambience at -6.6 dB, so ambience alone audibly ignored its saved level
        //           and played at the authored mixer volume. This Start pass is what makes it stick;
        //           it was lost when this method was changed from Start to Awake.
        //  Scene load — the container is built once and this component is DontDestroyOnLoad, so
        //           nothing else would ever refresh the mixer again for the rest of the session.
        //
        // All three funnel through the same LoadAndApplyVolumeSettings, so there is one implementation
        // of "what the volumes are", not three.
        private void Awake() => LoadAndApplyVolumeSettings();

        private void Start() => LoadAndApplyVolumeSettings();

        private void OnEnable() => SceneManager.sceneLoaded += HandleSceneLoaded;

        private void OnDisable() => SceneManager.sceneLoaded -= HandleSceneLoaded;

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => LoadAndApplyVolumeSettings();

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

            // The return value matters: a renamed or un-exposed parameter fails SILENTLY otherwise,
            // and the symptom — one channel stuck at its authored mixer level — looks like a settings
            // bug rather than a wiring one.
            if (!audioMixer.SetFloat(exposedParameter, Mathf.Log10(clampedVolume) * 20f))
                Debug.LogWarning($"{nameof(SoundMixerManager)}: mixer parameter '{exposedParameter}' is " +
                                 "not exposed on the AudioMixer — that channel's volume setting does " +
                                 "nothing. Expose it, or correct the name in SettingsKeys.", this);
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
