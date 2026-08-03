namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for global audio mixer volume control.
    /// Backed by <see cref="SoundMixerManager"/>.
    /// </summary>
    public interface ISoundMixerService
    {
        void SetMasterVolume(float volume);
        void SetMusicVolume(float volume);
        void SetSoundFXVolume(float volume);

        float GetSavedMasterVolume();
        float GetSavedMusicVolume();
        float GetSavedSoundFXVolume();

        // --- Added for the settings menu. Purely additive: the members above keep their exact
        // --- signatures so the legacy SettingsUI, which consumes this interface, is unaffected.

        void SetAmbienceVolume(float volume);
        void SetDialogueVolume(float volume);

        float GetSavedAmbienceVolume();
        float GetSavedDialogueVolume();

        /// <summary>Channel-driven form of the setters above, so callers can loop over channels.</summary>
        void SetVolume(AudioChannel channel, float volume);

        /// <summary>Channel-driven form of the getters above.</summary>
        float GetSavedVolume(AudioChannel channel);
    }
}
