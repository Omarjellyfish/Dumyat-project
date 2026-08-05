using UnityEngine;
using UnityEngine.Audio;

namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for pooled positional SFX. Injected in place of
    /// <c>SoundManager.Singleton</c>. Backed by <see cref="SoundManager"/>.
    /// </summary>
    public interface ISoundService
    {
        void PlaySoundFxAtPosition(AudioClip clip, Transform targetTransform, float volume, AudioMixerGroup mixerGroup = null);
        void PlayRandomSoundFxAtPosition(AudioClip[] clips, Transform targetTransform, float volume, AudioMixerGroup mixerGroup = null);
        void PlayLoopingSoundFxAtPosition(AudioClip clip, Transform targetTransform, float volume);
        void PlayVoiceLine(AudioClip clip, Transform targetTransform, float volume, AudioMixerGroup mixerGroup = null);
        void PlayUISoundFx(AudioClip clip, float volume = 1f);
        void PlaySceneTransitionSound();
    }
}
