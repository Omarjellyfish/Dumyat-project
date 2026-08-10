using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Plays one <see cref="AudioClip"/> at most once, optionally only in named scenes.
    ///
    /// Guidance lines are welcome the first time and grating on the twentieth, and several of them sit
    /// on objects the player handles repeatedly or that exist in more than one scene. This component
    /// pairs the clip with a <see cref="OnceOnlyCueGate"/> so the components that detect the gameplay
    /// MOMENT (a grab, a hook) only have to say <see cref="TryPlay"/> and stay about the gameplay.
    ///
    /// Use <see cref="OneShotDialogueCue"/> instead when the line belongs to a character and has a
    /// subtitle — that routes through the narrative system and can be interrupted and queued properly.
    /// This one is for sound that is not speech.
    /// </summary>
    [AddComponentMenu("Dumyat/Cues/One Shot Voice Cue")]
    public class OneShotVoiceCue : MonoBehaviour
    {
        [Header("Line")]
        [Tooltip("The clip to play. Leave empty to disable this cue.")]
        [SerializeField] private AudioClip _clip;

        [Tooltip("Playback volume, 0-1.")]
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;

        [Tooltip("Where the line plays from. Defaults to this GameObject — set it to a nearby " +
                 "character or prop when the voice should not appear to come from the object itself.")]
        [SerializeField] private Transform _playAt;

        [Header("Repeat")]
        [SerializeField] private OnceOnlyCueGate _gate = new OnceOnlyCueGate();

        private ISoundService _soundService;
        private IPlayerPersistenceService _persistence;

        /// <summary>The key this cue is remembered under. Falls back to the object name if unset.</summary>
        public string CueId => _gate.CueId(name);

        [Inject]
        private void Inject(ISoundService soundService, IPlayerPersistenceService persistence)
        {
            _soundService = soundService;
            _persistence = persistence;
        }

        /// <summary>
        /// Plays the line if it has not been heard yet and this scene allows it.
        /// Safe to call every trigger; returns whether anything was actually played.
        /// </summary>
        public bool TryPlay()
        {
            if (_clip == null) return false;
            if (!_gate.CanPlay(_persistence, name)) return false;

            if (_soundService == null)
            {
                // Injection is absent when a prefab was spawned at runtime without being injected, or
                // when the scene has no ContainerScope. Warn rather than throw: a missing voice line
                // must not take the grab that triggered it down with it.
                Debug.LogWarning($"{nameof(OneShotVoiceCue)} on '{name}' has no {nameof(ISoundService)} — " +
                                 $"the line '{CueId}' cannot play. Does this scene have a ContainerScope, " +
                                 "and was this prefab injected if it was spawned at runtime?", this);
                return false;
            }

            _soundService.PlaySoundFxAtPosition(_clip, _playAt != null ? _playAt : transform, _volume);
            _gate.MarkPlayed(_persistence, name, this);
            return true;
        }

        /// <summary>Forgets that this line played, so it can be heard again. For dev tools and testing.</summary>
        public void ResetCue() => _gate.ResetCue(name);
    }
}
