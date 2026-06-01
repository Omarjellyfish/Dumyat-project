using UnityEngine;
using UnityEngine.Pool;
using System.Collections;

namespace DumyatRuntime
{
    public class SoundFXManager : MonoBehaviour
    {
        public static SoundFXManager Singleton { get; private set; }

        [Header("Audio Sources")]
        [SerializeField] private AudioSource sfxAudioSourcePrefab;

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
        public void PlaySoundFxAtPosition(AudioClip clip, Transform targetTransform, float volume)
        {
            if (clip == null) return;

            // Grab an available AudioSource from the pool
            AudioSource source = audioPool.Get();

            source.transform.position = targetTransform.position;
            source.clip = clip;
            source.volume = volume;
            source.Play();

            // Start a coroutine to return it to the pool when finished
            StartCoroutine(ReturnToPoolAfterClip(source, clip.length));
        }

        public void PlayRandomSoundFxAtPosition(AudioClip[] clips, Transform targetTransform, float volume)
        {
            if (clips == null || clips.Length == 0) return;

            AudioClip clip = clips[Random.Range(0, clips.Length)];
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