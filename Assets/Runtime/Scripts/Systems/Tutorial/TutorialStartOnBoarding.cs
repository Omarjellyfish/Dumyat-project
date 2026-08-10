using System.Collections;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Starts the on-the-water tutorial when the player boards the boat.
    ///
    /// The fishing lesson is deliberately a SEPARATE sequence from the one on the shore, rather than
    /// more steps on the end of it. The two teach unrelated things and the player can arrive at the
    /// second without having done the first: skipping the walk-to-the-rod cards, or coming back to Ras
    /// El Bar on a later visit, used to mean the casting lesson never ran either, because it lived
    /// behind steps that were already spent. Boarding is the thing that should decide whether the
    /// casting lesson plays — not whether the player sat through the walk.
    /// </summary>
    [AddComponentMenu("Dumyat/Tutorial/Tutorial Start On Boarding")]
    public class TutorialStartOnBoarding : MonoBehaviour
    {
        [Tooltip("The sequence taught on the boat: casting, reeling, landing a fish.")]
        [SerializeField] private TutorialSequenceSO _sequence;

        [Tooltip("Start it again on every boarding. Off means only the first trip teaches. The sequence " +
                 "asset's own Persist Completion still decides whether it survives across sessions.")]
        [SerializeField] private bool _repeatOnEveryBoarding = true;

        private IEventService _eventService;
        private ITutorialService _tutorialService;
        private bool _startedOnce;

        [Inject]
        private void Inject(IEventService eventService, ITutorialService tutorialService)
        {
            _eventService = eventService;
            _tutorialService = tutorialService;
        }

        private void OnEnable() => Subscribe();

        // Reflex injects from ContainerScope.Awake, which can land after another object's OnEnable.
        private void Start() => Subscribe();

        private void OnDisable() => _eventService?.Unsubscribe<BoatBoardedEvent>(OnBoarded);

        private void Subscribe()
        {
            if (_eventService == null) return;
            _eventService.Unsubscribe<BoatBoardedEvent>(OnBoarded);
            _eventService.Subscribe<BoatBoardedEvent>(OnBoarded);
        }

        private void OnBoarded(BoatBoardedEvent evt)
        {
            if (_sequence == null)
            {
                Debug.LogWarning($"{nameof(TutorialStartOnBoarding)} on '{name}' has no sequence to start.", this);
                return;
            }

            if (!_repeatOnEveryBoarding && _startedOnce) return;
            _startedOnce = true;

            StartCoroutine(StartNextFrame());
        }

        /// <summary>
        /// One frame's delay, because the shore sequence's last card also completes on
        /// <see cref="BoatBoardedEvent"/> and <c>TutorialService.StartTutorial</c> refuses to start
        /// while another sequence is still active. Handler order between the two is not defined, so
        /// waiting is what makes this work regardless of which runs first.
        /// </summary>
        private IEnumerator StartNextFrame()
        {
            yield return null;

            if (_tutorialService == null)
            {
                Debug.LogWarning($"{nameof(TutorialStartOnBoarding)} on '{name}': no TutorialService — " +
                                 "does this scene have a ContainerScope?", this);
                yield break;
            }

            // force: the boat lesson is independent of whatever was happening ashore. If the shore
            // sequence is still on screen when the player boards it is now irrelevant, and if it was
            // skipped or already finished there is nothing to defer to — either way the casting lesson
            // must run. Without force, StartTutorial declines whenever another sequence is active.
            _tutorialService.StartTutorial(_sequence, force: true);
        }
    }
}
