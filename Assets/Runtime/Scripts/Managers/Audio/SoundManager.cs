using UnityEngine;
using UnityEngine.Pool;
using System.Collections;
using UnityEngine.Audio;
namespace DumyatRuntime
{
    public class SoundManager : MonoBehaviour, ISoundService
    {
        [Header("Audio Sources")]
        [SerializeField] private AudioSource _sfxAudioSourcePrefab;
        [SerializeField] private AudioMixerGroup _defaultMixerGroup;

        private IObjectPool<AudioSource> _audioPool;

        /// <summary>
        /// Lazy-initialized pool. Guarantees the pool exists regardless of Unity's
        /// Awake execution order (Reflex's ContainerScope runs before child Awake).
        /// </summary>
        private IObjectPool<AudioSource> AudioPool
        {
            get
            {
                if (_audioPool == null)
                {
                    _audioPool = new ObjectPool<AudioSource>(
                        createFunc: CreatePooledItem,
                        actionOnGet: OnTakeFromPool,
                        actionOnRelease: OnReturnedToPool,
                        actionOnDestroy: OnDestroyPoolObject,
                        collectionCheck: false,
                        defaultCapacity: 10,
                        maxSize: 30
                    );
                }
                return _audioPool;
            }
        }

        #region Pooling Logic
        private AudioSource CreatePooledItem()
        {
            AudioSource source = Instantiate(_sfxAudioSourcePrefab, transform);
            return source;
        }

        private void OnTakeFromPool(AudioSource source)
        {
            // Reset state a previous user may have left behind: PlayVoiceLine sets spatialBlend to 1
            // and PlayLoopingSoundFxAtPosition sets loop to true, and neither ever restores them. Without
            // this, a recycled source stays 3D/looping forever and later one-shots come out attenuated.
            source.spatialBlend = 0f;
            source.loop = false;
            source.gameObject.SetActive(true);
        }

        private void OnReturnedToPool(AudioSource source)
        {
            source.gameObject.SetActive(false);
        }

        private void OnDestroyPoolObject(AudioSource source)
        {
            Destroy(source.gameObject);
        }
        #endregion

        #region Public API
        public void PlaySoundFxAtPosition(AudioClip clip, Transform targetTransform, float volume, AudioMixerGroup mixerGroup = null)
        {
            if (clip == null) return;

            AudioSource source = AudioPool.Get();

            source.transform.position = targetTransform.position;
            source.clip = clip;
            source.volume = volume;
            if (mixerGroup != null)
                source.outputAudioMixerGroup = mixerGroup;
            else
                source.outputAudioMixerGroup = _defaultMixerGroup;

            source.Play();

            StartCoroutine(ReturnToPoolAfterClip(source, clip.length));
        }

        public void PlayRandomSoundFxAtPosition(AudioClip[] clips, Transform targetTransform, float volume, AudioMixerGroup mixerGroup = null)
        {
            if (clips == null || clips.Length == 0) return;

            AudioClip clip = clips[Random.Range(0, clips.Length)];
            PlaySoundFxAtPosition(clip, targetTransform, volume, mixerGroup);
        }

        public void PlayLoopingSoundFxAtPosition(AudioClip clip, Transform targetTransform, float volume)
        {
            if (clip == null) return;
            AudioSource source = AudioPool.Get();
            source.transform.position = targetTransform.position;
            source.clip = clip;
            source.volume = volume;
            source.loop = true;
            source.Play();
        }

        public void PlayVoiceLine(AudioClip clip, Transform targetTransform, float volume, AudioMixerGroup mixerGroup = null)
        {
            if (clip == null) return;

            AudioSource source = AudioPool.Get();

            if (targetTransform != null)
            {
                source.transform.position = targetTransform.position;
                source.spatialBlend = 1f; // 3D positional
            }
            else
            {
                source.spatialBlend = 0f; // 2D global fallback
            }

            source.clip = clip;
            source.volume = volume;
            source.outputAudioMixerGroup = mixerGroup != null ? mixerGroup : _defaultMixerGroup;
            source.Play();

            StartCoroutine(ReturnToPoolAfterClip(source, clip.length));
        }
        #endregion

        private IEnumerator ReturnToPoolAfterClip(AudioSource source, float delay)
        {
            yield return new WaitForSeconds(delay);


            if (source != null)
            {
                source.Stop();
                source.clip = null;
                AudioPool.Release(source);
            }
        }
    }
}