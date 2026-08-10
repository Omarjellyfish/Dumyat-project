using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Keeps the running count of baskets scored this session and republishes it as
    /// <see cref="HoopScoreChangedEvent"/>. One per scene, on any GameObject — it needs no placement.
    ///
    /// Counting is split away from <see cref="HoopScoreZone"/> so a scene can have two hoops feeding one
    /// score, and away from <see cref="HoopScoreAnnouncer"/> so the number can be shown somewhere else
    /// (a scoreboard, a quest) without the announcer being involved.
    ///
    /// The total is deliberately NOT persisted — see <see cref="HoopScoreChangedEvent"/>.
    /// </summary>
    public class HoopScoreTracker : MonoBehaviour
    {
        private IEventService _events;

        /// <summary>Baskets scored since this scene loaded.</summary>
        public int Total { get; private set; }

        [Inject]
        private void Inject(IEventService events) => _events = events;

        private void OnEnable()
        {
            if (_events == null)
            {
                Debug.LogWarning($"{nameof(HoopScoreTracker)} on '{name}' has no IEventService injected — " +
                                 "baskets will not be counted. Does this scene have a ContainerScope?", this);
                return;
            }
            _events.Subscribe<HoopScoredEvent>(HandleHoopScored);
        }

        private void OnDisable() => _events?.Unsubscribe<HoopScoredEvent>(HandleHoopScored);

        // Cached method group so Unsubscribe matches (see CLAUDE.md, Events).
        private void HandleHoopScored(HoopScoredEvent evt)
        {
            Total++;
            _events?.Publish(new HoopScoreChangedEvent(Total, evt.Position));
        }
    }
}
