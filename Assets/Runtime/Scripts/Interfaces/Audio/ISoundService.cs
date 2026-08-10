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

        /// <summary>
        /// Silences every pooled source and returns them all.
        ///
        /// The pool lives on a DontDestroyOnLoad object, so a clip started in one scene plays straight
        /// through the load and into the next — a fisherman's line finishing over the market. Leaving a
        /// scene is the one moment that must never happen, and no caller can be relied on to stop its
        /// own clip on the way out.
        ///
        /// Safe to call on <c>SceneLoadBeginEvent</c>: <see cref="SceneLoadService"/> publishes that
        /// BEFORE it starts the transition sting, so the sting is unaffected.
        /// </summary>
        void StopAll();

        /// <summary>
        /// Plays the loading/transition sting and returns how long it will last, in seconds — 0 when
        /// nothing was played.
        ///
        /// The length is returned rather than fired-and-forgotten because
        /// <see cref="SceneLoadService"/> treats this clip as part of the load: it holds scene
        /// activation until the clip has finished, so the sting always lands over the loading screen
        /// instead of bleeding into the first seconds of the destination.
        /// </summary>
        float PlaySceneTransitionSound();
    }
}
