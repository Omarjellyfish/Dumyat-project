using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Plays one <see cref="DialogueSequenceSO"/> at most once, optionally only in named scenes.
    ///
    /// The narrative twin of <see cref="OneShotVoiceCue"/>, sharing its <see cref="OnceOnlyCueGate"/>.
    /// Prefer this one for anything a character says: it goes through <c>NarrativeService</c>, so the
    /// line gets its speaker, its subtitle, its priority, and proper interruption and queueing against
    /// whatever else is talking — a raw clip gets none of that and will happily play over the father
    /// mid-sentence.
    ///
    /// This component does not decide WHEN. Pair it with <see cref="DialogueCueOnGrab"/> or
    /// <see cref="DialogueCueOnZoneEnter"/>, or call <see cref="TryPlay"/> from the system that owns
    /// the moment — see <c>HookingFish</c> and <c>BoatTransit</c>.
    /// </summary>
    [AddComponentMenu("Dumyat/Cues/One Shot Dialogue Cue")]
    public class OneShotDialogueCue : MonoBehaviour
    {
        [Header("Line")]
        [Tooltip("The sequence to play. Leave empty to disable this cue.")]
        [SerializeField] private DialogueSequenceSO _sequence;

        [SerializeField] private DialoguePriority _priority = DialoguePriority.Normal;

        [Header("Repeat")]
        [SerializeField] private OnceOnlyCueGate _gate = new OnceOnlyCueGate();

        private IEventService _eventService;
        private IPlayerPersistenceService _persistence;

        /// <summary>The key this cue is remembered under. Falls back to the object name if unset.</summary>
        public string CueId => _gate.CueId(name);

        [Inject]
        private void Inject(IEventService eventService, IPlayerPersistenceService persistence)
        {
            _eventService = eventService;
            _persistence = persistence;
        }

        /// <summary>
        /// Plays the sequence if it has not been heard yet and this scene allows it.
        /// Safe to call every trigger; returns whether anything was actually published.
        /// </summary>
        public bool TryPlay()
        {
            if (_sequence == null) return false;
            if (!_gate.CanPlay(_persistence, name)) return false;

            if (_eventService == null)
            {
                Debug.LogWarning($"{nameof(OneShotDialogueCue)} on '{name}' has no {nameof(IEventService)} — " +
                                 $"the line '{CueId}' cannot play. Does this scene have a ContainerScope, " +
                                 "and was this prefab injected if it was spawned at runtime?", this);
                return false;
            }

            _eventService.Publish(new PlayDialogueEvent(_sequence, _priority));
            _gate.MarkPlayed(_persistence, name, this);
            return true;
        }

        /// <summary>Forgets that this line played, so it can be heard again. For dev tools and testing.</summary>
        public void ResetCue() => _gate.ResetCue(name);
    }
}
