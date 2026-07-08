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
    }
}
