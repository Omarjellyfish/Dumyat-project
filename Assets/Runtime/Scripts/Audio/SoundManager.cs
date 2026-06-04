using UnityEngine;
using UnityEngine.Pool;
using System.Collections;
using UnityEngine.Audio;
namespace DumyatRuntime
{
    public class SoundManager : MonoBehaviour
    {
        public static SoundManager Singleton { get; private set; }

        [Header("Audio Sources")]
        [SerializeField] private AudioSource sfxAudioSourcePrefab;
        [SerializeField] private AudioSource voiceAudioSourcePrefab;
        [SerializeField] private AudioMixerGroup defaultMixerGroup; 

        // The Object Pool
        private IObjectPool<AudioSource> audioPool;

        private void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }
            Singleton = this;
            DontDestroyOnLoad(gameObject);

            // Initialize the pool
            audioPool = new ObjectPool<AudioSource>(
                createFunc: CreatePooledItem,
                actionOnGet: OnTakeFromPool,
                actionOnRelease: OnReturnedToPool,
                actionOnDestroy: OnDestroyPoolObject,
                collectionCheck: false,
                defaultCapacity: 10, // Start with 10 sources ready to go
                maxSize: 30 // Don't let it grow infinitely
            );
        }

        #region Pooling Logic
        private AudioSource CreatePooledItem()
        {
            // Create a new AudioSource under this manager's hierarchy
            AudioSource source = Instantiate(sfxAudioSourcePrefab, transform);
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

            // Grab an available AudioSource from the pool
            AudioSource source = audioPool.Get();

            source.transform.position = targetTransform.position;
            source.clip = clip;
            source.volume = volume;
            if (mixerGroup != null)
                source.outputAudioMixerGroup = mixerGroup;
            else
                source.outputAudioMixerGroup = defaultMixerGroup;

            source.Play();

            // Start a coroutine to return it to the pool when finished
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
            AudioSource source = audioPool.Get();
            source.transform.position = targetTransform.position;
            source.clip = clip;
            source.volume = volume;
            source.loop = true;
            source.Play();
        }

        public void PlayVoiceLine(AudioClip clip, Transform targetTransform, float volume)
        {
            PlaySoundFxAtPosition(clip, targetTransform, volume);
        }
        #endregion

        // Coroutine to handle returning the source safely
        private IEnumerator ReturnToPoolAfterClip(AudioSource source, float delay)
        {
            yield return new WaitForSeconds(delay);

            // Only release it back if it hasn't been destroyed by scene loads
            if (source != null)
            {
                source.Stop();
                source.clip = null;
                audioPool.Release(source);
            }
        }
    }
}