using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// Turns the reel's continuous rotation into a discrete "the player wound the reel" announcement.
    ///
    /// Deliberately separate from <see cref="FishingLineController"/>, which also watches the reel:
    /// that one runs its winch in FixedUpdate and reacts every physics tick, which is far too noisy to
    /// put on the EventBus. It also reads the reel pivot's euler angle, whereas this reads
    /// <see cref="ReelWheel.CurrentRotation"/> — the value the player's hand actually accumulates, and
    /// therefore the one that answers "did they use the reel".
    ///
    /// Reports in fixed chunks so a slow, steady wind produces the same stream of events as a fast one.
    /// </summary>
    public class ReelTurnReporter : MonoBehaviour
    {
        [Tooltip("The reel being watched. Auto-found on this GameObject or its children if left empty.")]
        [SerializeField] private ReelWheel _reel;

        [Tooltip("Degrees of turning — in either direction — that count as one 'the player used the " +
                 "reel' report. Roughly a quarter turn by default: enough to be a deliberate action " +
                 "rather than a knock.")]
        [SerializeField, Min(1f)] private float _degreesPerReport = 90f;

        private IEventService _eventService;
        private float _previousRotation;
        private float _accumulated;

        [Inject]
        private void Inject(IEventService eventService) => _eventService = eventService;

        private void Awake()
        {
            if (_reel == null) _reel = GetComponentInChildren<ReelWheel>(true);

            if (_reel == null)
            {
                Debug.LogWarning($"'{name}' has no ReelWheel to watch — reel turns will not be " +
                                 "announced. Assign one in the Inspector.", this);
                enabled = false;
                return;
            }

            _previousRotation = _reel.CurrentRotation;
        }

        private void Update()
        {
            float rotation = _reel.CurrentRotation;
            _accumulated += Mathf.Abs(rotation - _previousRotation);
            _previousRotation = rotation;

            if (_accumulated < _degreesPerReport) return;

            float reported = _accumulated;
            // Subtract rather than zero, so a single fast frame that overshoots several thresholds
            // does not silently discard the remainder.
            _accumulated -= _degreesPerReport;

            _eventService?.Publish(new ReelTurnedEvent(reported));
        }
    }
}
