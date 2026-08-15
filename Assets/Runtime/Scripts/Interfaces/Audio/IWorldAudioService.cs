using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for world music/ambient crossfade playback. Injected in place of
    /// <c>WorldAudioManager.Singleton</c>. Backed by the scene-bound <see cref="WorldAudioManager"/>.
    /// </summary>
    public interface IWorldAudioService
    {
        void PlayMusic(AudioClip newClip, float fadeDuration = -1f, float targetVolume = 1.0f);
        void StopMusic(float fadeDuration = -1f);
        void PlayAmbient(AudioClip newClip, float fadeDuration = -1f, float targetVolume = 1.0f);
        void StopAmbient(float fadeDuration = -1f);
    }
}
