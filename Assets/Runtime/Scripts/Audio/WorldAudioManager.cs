using System.Collections;
using UnityEngine;

namespace DumyatRuntime
{
    public class WorldAudioManager : MonoBehaviour
    {
        public static WorldAudioManager Singleton { get; private set; }

        [Header("Music Sources (A/B Setup)")]
        [SerializeField] private AudioSource musicSourceA;
        [SerializeField] private AudioSource musicSourceB;

        [Header("Ambient Sources (A/B Setup)")]
        [SerializeField] private AudioSource ambientSourceA;
        [SerializeField] private AudioSource ambientSourceB;

        [Header("Global Settings")]
        [SerializeField] private float defaultFadeDuration = 2.0f;

        private AudioSource activeMusicSource;
        private AudioSource activeAmbientSource;

        private Coroutine currentMusicFadeCoroutine;
        private Coroutine currentAmbientFadeCoroutine;

        private void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }
            Singleton = this;
            DontDestroyOnLoad(gameObject);

            activeMusicSource = musicSourceA;
            activeAmbientSource = ambientSourceA;
        }

        #region Music Logic
        public void PlayMusic(AudioClip newClip, float fadeDuration = -1f)
        {
            if (newClip == null) return;
            if (activeMusicSource.clip == newClip && activeMusicSource.isPlaying) return;

            float duration = fadeDuration < 0 ? defaultFadeDuration : fadeDuration;
            AudioSource fadingInSource = (activeMusicSource == musicSourceA) ? musicSourceB : musicSourceA;
            AudioSource fadingOutSource = activeMusicSource;

            if (currentMusicFadeCoroutine != null) StopCoroutine(currentMusicFadeCoroutine);

            currentMusicFadeCoroutine = StartCoroutine(Crossfade(fadingOutSource, fadingInSource, newClip, duration));
            activeMusicSource = fadingInSource;
        }
        #endregion

        #region Ambient Logic
        public void PlayAmbient(AudioClip newClip, float fadeDuration = -1f)
        {
            if (newClip == null) return;

            // This check prevents audio stuttering if the player walks back and forth across a zone border
            if (activeAmbientSource.clip == newClip && activeAmbientSource.isPlaying) return;

            float duration = fadeDuration < 0 ? defaultFadeDuration : fadeDuration;
            AudioSource fadingInSource = (activeAmbientSource == ambientSourceA) ? ambientSourceB : ambientSourceA;
            AudioSource fadingOutSource = activeAmbientSource;

            if (currentAmbientFadeCoroutine != null) StopCoroutine(currentAmbientFadeCoroutine);

            currentAmbientFadeCoroutine = StartCoroutine(Crossfade(fadingOutSource, fadingInSource, newClip, duration));
            activeAmbientSource = fadingInSource;
        }
        #endregion

        // Generic crossfade coroutine used by both Music and Ambient systems
        private IEnumerator Crossfade(AudioSource fadeOut, AudioSource fadeIn, AudioClip newClip, float duration)
        {
            fadeIn.clip = newClip;
            fadeIn.volume = 0f;
            fadeIn.Play();

            float startVolumeFadeOut = fadeOut.volume;
            float timeElapsed = 0f;

            while (timeElapsed < duration)
            {
                timeElapsed += Time.deltaTime;
                float t = timeElapsed / duration;

                fadeIn.volume = Mathf.Lerp(0f, 1f, t);
                fadeOut.volume = Mathf.Lerp(startVolumeFadeOut, 0f, t);

                yield return null;
            }

            fadeIn.volume = 1f;
            fadeOut.volume = 0f;
            fadeOut.Stop();
        }
    }
}