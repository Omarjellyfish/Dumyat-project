using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Place on NPC prefabs that can speak. Registers with <see cref="INpcRegistryService"/>
    /// so the <see cref="DialoguePresentationManager"/> can find this NPC and play voice lines
    /// on its dedicated AudioSource (enabling future lipsync).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class NarrativeCharacterView : MonoBehaviour, INpcActor
    {
        [Header("Character Identity")]
        [SerializeField] private CharacterDialogueDataSO _characterData;

        [Header("Audio")]
        [SerializeField] private AudioSource _voiceAudioSource;

        private INpcRegistryService _registry;

        [Inject]
        private void Inject(INpcRegistryService registry)
        {
            _registry = registry;
        }

        private void OnEnable()
        {
            if (_characterData == null)
            {
                Debug.LogWarning($"[NarrativeCharacterView] No CharacterDialogueDataSO on '{name}'.", this);
                return;
            }
            _registry.Register(_characterData.CharacterId, this);
        }

        private void OnDisable()
        {
            if (_characterData == null) return;
            _registry.Unregister(_characterData.CharacterId);
        }

        public void PlayVoiceLine(AudioClip clip, float volume = 1f)
        {
            if (clip == null) return;

            _voiceAudioSource.Stop();
            _voiceAudioSource.clip = clip;
            _voiceAudioSource.volume = volume;
            _voiceAudioSource.Play();
        }

        public void StopVoiceLine()
        {
            _voiceAudioSource.Stop();
            _voiceAudioSource.clip = null;
        }
    }
}
