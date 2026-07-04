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

        private void Awake()
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

        #region Pooling Logic
        private AudioSource CreatePooledItem()
        {
            AudioSource source = Instantiate(_sfxAudioSourcePrefab, transform);
            return source;
        }

        private void OnTakeFromPool(AudioSource source)
        {
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

            AudioSource source = _audioPool.Get();

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
            AudioSource source = _audioPool.Get();
            source.transform.position = targetTransform.position;
            source.clip = clip;
            source.volume = volume;
            source.loop = true;
            source.Play();
        }

        public void PlayVoiceLine(AudioClip clip, Transform targetTransform, float volume, AudioMixerGroup mixerGroup = null)
        {
            PlaySoundFxAtPosition(clip, targetTransform, volume, mixerGroup);
        }
        #endregion

        private IEnumerator ReturnToPoolAfterClip(AudioSource source, float delay)
        {
            yield return new WaitForSeconds(delay);


            if (source != null)
            {
                source.Stop();
                source.clip = null;
                _audioPool.Release(source);
            }
        }
    }
}