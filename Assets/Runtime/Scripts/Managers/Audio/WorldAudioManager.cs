using System.Collections;
using UnityEngine;

namespace DumyatRuntime
{
    public class WorldAudioManager : MonoBehaviour, IWorldAudioService
    {
        [Header("Music Sources (A/B Setup)")]
        [SerializeField] private AudioSource _musicSourceA;
        [SerializeField] private AudioSource _musicSourceB;

        [Header("Ambient Sources (A/B Setup)")]
        [SerializeField] private AudioSource _ambientSourceA;
        [SerializeField] private AudioSource _ambientSourceB;

        [Header("Global Settings")]
        [SerializeField] private float defaultFadeDuration = 2.0f;

        private AudioSource _activeMusicSource;
        private AudioSource _activeAmbientSource;

        private Coroutine _currentMusicFadeCoroutine;
        private Coroutine _currentAmbientFadeCoroutine;

        private void Awake()
        {
            _activeMusicSource = _musicSourceA;
            _activeAmbientSource = _ambientSourceA;
        }

        #region Music Logic
        public void PlayMusic(AudioClip newClip, float fadeDuration = -1f)
        {
            if (newClip == null) return;
            if (_activeMusicSource.clip == newClip && _activeMusicSource.isPlaying) return;

            float duration = fadeDuration < 0 ? defaultFadeDuration : fadeDuration;
            AudioSource fadingInSource = (_activeMusicSource == _musicSourceA) ? _musicSourceB : _musicSourceA;
            AudioSource fadingOutSource = _activeMusicSource;

            if (_currentMusicFadeCoroutine != null) StopCoroutine(_currentMusicFadeCoroutine);

            _currentMusicFadeCoroutine = StartCoroutine(Crossfade(fadingOutSource, fadingInSource, newClip, duration));
            _activeMusicSource = fadingInSource;
        }
        #endregion

        #region Ambient Logic
        public void PlayAmbient(AudioClip newClip, float fadeDuration = -1f)
        {
            if (newClip == null) return;

            // This check prevents audio stuttering if the player walks back and forth across a zone border
            if (_activeAmbientSource.clip == newClip && _activeAmbientSource.isPlaying) return;

            float duration = fadeDuration < 0 ? defaultFadeDuration : fadeDuration;
            AudioSource fadingInSource = (_activeAmbientSource == _ambientSourceA) ? _ambientSourceB : _ambientSourceA;
            AudioSource fadingOutSource = _activeAmbientSource;
            if (_currentAmbientFadeCoroutine != null) StopCoroutine(_currentAmbientFadeCoroutine);

            _currentAmbientFadeCoroutine = StartCoroutine(Crossfade(fadingOutSource, fadingInSource, newClip, duration));
            _activeAmbientSource = fadingInSource;
        }
        #endregion

        // Generic crossfade coroutine used by both Music and Ambient systems
        private IEnumerator Crossfade(AudioSource fadeOut, AudioSource fadeIn, AudioClip newClip, float duration)
        {
            fadeIn.clip = newClip;
            fadeIn.volume = 0f;
            fadeIn.loop = true;
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