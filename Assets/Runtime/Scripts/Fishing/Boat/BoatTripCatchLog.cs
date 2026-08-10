using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Counts what the player has pulled out of the water on the current boat trip, so the gunwale
    /// board can answer "is it worth heading back yet" without them opening their bag.
    ///
    /// A tally, not a container: a caught fish goes into the player's own inventory when they pick it
    /// up, and holding a second parallel list of the same fish would be two places to keep in step for
    /// no gain. This only ever adds up <see cref="FishCaughtEvent"/> — it owns no fish and can lose
    /// none.
    ///
    /// The count resets on <see cref="BoatBoardedEvent"/> because it describes a trip: rowing out again
    /// starts a new one, and a number that silently accumulated across every trip of the day would not
    /// be the thing the board claims to show.
    /// </summary>
    [AddComponentMenu("Dumyat/Fishing/Boat Trip Catch Log")]
    public class BoatTripCatchLog : MonoBehaviour
    {
        /// <summary>Fish caught since this trip began.</summary>
        public int Count { get; private set; }

        /// <summary>Combined weight (kg) of those fish.</summary>
        public float TotalWeightKg { get; private set; }

        private IEventService _eventService;

        [Inject]
        private void Inject(IEventService eventService) => _eventService = eventService;

        private void OnEnable() => Subscribe();

        // Reflex injects scene objects from ContainerScope.Awake, which can run after another object's
        // OnEnable — so the subscription is retried here, idempotently.
        private void Start() => Subscribe();

        private void OnDisable()
        {
            if (_eventService == null) return;
            _eventService.Unsubscribe<FishCaughtEvent>(OnFishCaught);
            _eventService.Unsubscribe<BoatBoardedEvent>(OnBoarded);
        }

        private void Subscribe()
        {
            if (_eventService == null) return;

            _eventService.Unsubscribe<FishCaughtEvent>(OnFishCaught);
            _eventService.Unsubscribe<BoatBoardedEvent>(OnBoarded);

            _eventService.Subscribe<FishCaughtEvent>(OnFishCaught);
            _eventService.Subscribe<BoatBoardedEvent>(OnBoarded);
        }

        /// <summary>Starts a fresh trip. Called on boarding; public for dev tools.</summary>
        public void ResetTrip()
        {
            Count = 0;
            TotalWeightKg = 0f;
        }

        private void OnBoarded(BoatBoardedEvent evt) => ResetTrip();

        private void OnFishCaught(FishCaughtEvent evt)
        {
            Count++;
            if (evt.Fish != null) TotalWeightKg += evt.Fish.Weight;
        }
    }
}
