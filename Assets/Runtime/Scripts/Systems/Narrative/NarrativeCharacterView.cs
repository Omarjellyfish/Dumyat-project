using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Audio;

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

        [Tooltip("Optional. Only used if the AudioSource above has no output group of its own — " +
                 "assign the MainMixer 'Dialouge' group here to rescue an NPC prefab that was " +
                 "authored without one. Leave empty when the source is already routed.")]
        [SerializeField] private AudioMixerGroup _voiceMixerGroup;

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

            // The source is a prefab detail no code here can see, and an unrouted one bypasses the
            // mixer completely — it would ignore the Voice slider AND Master. Checked per line rather
            // than once in OnEnable because an NPC spawned at runtime, or one whose source is swapped
            // by an animation, would slip past a one-time check.
            VoiceMixerRouting.EnsureRouted(_voiceAudioSource, _voiceMixerGroup, this);

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
