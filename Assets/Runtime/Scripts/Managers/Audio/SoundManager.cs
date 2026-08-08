using UnityEngine;
using UnityEngine.Pool;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Audio;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    public class SoundManager : MonoBehaviour, ISoundService
    {
        [Header("Audio Sources")]
        [SerializeField] private AudioSource _sfxAudioSourcePrefab;
        [SerializeField] private AudioMixerGroup _defaultMixerGroup;

        [Tooltip("Mixer group spoken lines are routed through — the 'Dialouge' group of MainMixer. " +
                 "Kept separate from the default group so voice obeys the Voice slider in Settings " +
                 "rather than whichever channel general SFX happen to be on.")]
        [SerializeField] private AudioMixerGroup _voiceMixerGroup;

        [Header("Scene Transition Audio")]
        [SerializeField] private AudioClip[] _sceneTransitionClips;
        [SerializeField] private float _transitionVolume = 1f;

        [Header("UI Audio")]
        [SerializeField] private AudioClip _uiHoverClip;
        [SerializeField] private AudioClip _uiClickClip;
        [SerializeField] private float _uiVolume = 1f;

        private IObjectPool<AudioSource> _audioPool;
        private HashSet<UIDocument> _registeredUIDocuments = new HashSet<UIDocument>();

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
        private void Start()
        {
            StartCoroutine(PollUIDocuments());
        }

        private IEnumerator PollUIDocuments()
        {
            while (true)
            {
                RegisterAllUIDocuments();
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        private void RegisterAllUIDocuments()
        {
            UIDocument[] documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _registeredUIDocuments.RemoveWhere(doc => doc == null);

            foreach (var doc in documents)
            {
                if (doc != null && doc.rootVisualElement != null)
                {
                    if (_registeredUIDocuments.Add(doc))
                    {
                        doc.rootVisualElement.RegisterCallback<PointerEnterEvent>(OnPointerEnter, TrickleDown.TrickleDown);
                        doc.rootVisualElement.RegisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
                    }
                }
            }
        }

        private void OnPointerEnter(PointerEnterEvent evt)
        {
            if (_uiHoverClip == null) return;
            if (evt.target is Button || evt.target is Toggle || evt.target is DropdownField || evt.target is RadioButton)
            {
                PlayUISoundFx(_uiHoverClip, _uiVolume);
            }
        }

        private void OnClick(ClickEvent evt)
        {
            if (_uiClickClip == null) return;
            if (evt.target is Button || evt.target is Toggle || evt.target is DropdownField || evt.target is RadioButton)
            {
                PlayUISoundFx(_uiClickClip, _uiVolume);
            }
        }

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

            // Same reasoning for the mixer group, and it matters more now that PlayVoiceLine routes to
            // Dialogue rather than the default: PlayLoopingSoundFxAtPosition never assigns a group at
            // all, so a source recycled from a voice line would leave an ambient loop following the
            // Voice slider.
            source.outputAudioMixerGroup = _defaultMixerGroup;

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

            // Voice defaults to the Dialogue group, NOT to _defaultMixerGroup. Every narrative caller
            // — the 2D fallback in DialoguePresentationManager, TutorialRunner — passes no group, and
            // the general-purpose default is the mixer's Master group, so all of them used to obey
            // Master while ignoring the Voice slider entirely.
            source.outputAudioMixerGroup =
                VoiceMixerRouting.ResolveGroup(mixerGroup, _voiceMixerGroup, _defaultMixerGroup, this);

            source.Play();

            StartCoroutine(ReturnToPoolAfterClip(source, clip.length));
        }

        public void PlayUISoundFx(AudioClip clip, float volume = 1f)
        {
            if (clip == null) return;
            AudioSource source = AudioPool.Get();
            source.spatialBlend = 0f; // 2D purely
            source.clip = clip;
            source.volume = volume;
            source.outputAudioMixerGroup = _defaultMixerGroup;
            source.Play();
            StartCoroutine(ReturnToPoolAfterClip(source, clip.length));
        }

        public float PlaySceneTransitionSound()
        {
            if (_sceneTransitionClips == null || _sceneTransitionClips.Length == 0) return 0f;

            AudioClip clip = _sceneTransitionClips[Random.Range(0, _sceneTransitionClips.Length)];
            if (clip == null) return 0f;

            PlayUISoundFx(clip, _transitionVolume);

            // The caller gates scene activation on this — see ISoundService.PlaySceneTransitionSound.
            return clip.length;
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