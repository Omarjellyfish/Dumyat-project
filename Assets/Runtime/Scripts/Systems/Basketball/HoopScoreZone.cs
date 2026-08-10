using System.Collections.Generic;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Counts a basket when a <see cref="ScoreableBall"/> passes DOWNWARD through the rim, and publishes
    /// <see cref="HoopScoredEvent"/>. Put it on an empty child of the hoop, positioned at the centre of
    /// the rim ring, with a trigger collider generous enough to contain the ball just above and just
    /// below the rim plane.
    ///
    /// It does NOT score on plain trigger overlap. A trigger volume alone cannot tell a made basket from
    /// a ball tossed up through the ring from underneath, or from one that rattles around the rim and
    /// bounces back out — both overlap identically. Instead the ball's centre is sampled every physics
    /// step and a basket is registered only when the segment between two samples crosses the rim plane
    /// going down, inside <see cref="_rimRadius"/> of the axis. That is a real plane crossing, so it is
    /// immune to how coarsely the rim colliders approximate the ring, and it cannot be fooled by a ball
    /// resting in the net.
    ///
    /// Sampling the SEGMENT rather than the instantaneous position also survives a fast shot: a ball
    /// moving 6 m/s covers 12 cm in a physics step and can be above the rim on one frame and below it on
    /// the next without ever being measured inside the ring.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HoopScoreZone : MonoBehaviour
    {
        [Header("Rim")]
        [Tooltip("How far the BALL'S CENTRE may be from the rim axis and still count, in metres — not " +
                 "the rim's own radius. A ball that physically fits through is geometrically limited to " +
                 "(rim inner radius − ball radius); set this a little above that so a real basket is " +
                 "never missed, and far below the rim radius so a ball deflecting down OUTSIDE the ring " +
                 "is rejected. Home hoop: rim inner 0.233, ball radius 0.168, so 0.065 is the maximum " +
                 "and 0.09 is the tolerant setting.")]
        [SerializeField] private float _scoreRadius = 0.09f;

        [Tooltip("Height of the rim plane relative to this transform. Leave at 0 when the transform sits " +
                 "on the rim itself.")]
        [SerializeField] private float _rimPlaneOffset;

        [Header("Rules")]
        [Tooltip("Ignore balls that are still held. Without this the player can carry the ball through " +
                 "the hoop by hand and farm baskets — XR grab makes the Rigidbody kinematic while held, " +
                 "which is what this detects.")]
        [SerializeField] private bool _ignoreHeldBalls = true;

        [Tooltip("Seconds before the same ball can score again. Stops a ball that settles in the net and " +
                 "jitters across the plane from scoring repeatedly.")]
        [SerializeField] private float _rescoreCooldown = 0.75f;

        [Header("Feedback")]
        [Tooltip("Optional swish/net sounds, one picked at random on every basket. Leave empty for silence.")]
        [SerializeField] private AudioClip[] _netSounds;

        [Range(0f, 1f)]
        [SerializeField] private float _netVolume = 0.85f;

        private IEventService _events;
        private ISoundService _sounds;

        // Ball -> centre position at the previous physics step. Only balls currently inside the trigger
        // are tracked, so the per-step cost is proportional to balls in the hoop, not balls in the scene.
        private readonly Dictionary<Rigidbody, Vector3> _lastCentre = new Dictionary<Rigidbody, Vector3>();
        private readonly Dictionary<Rigidbody, float> _nextScoreTime = new Dictionary<Rigidbody, float>();

        // Scratch buffer, reused every step: the dictionary cannot be mutated while it is being
        // enumerated, so the keys are copied out first. A field rather than a local to keep FixedUpdate
        // allocation-free.
        private readonly List<Rigidbody> _tracked = new List<Rigidbody>();

        /// <summary>World-space centre of the rim ring.</summary>
        public Vector3 RimCentre => transform.position + Vector3.up * _rimPlaneOffset;

        /// <summary>Maximum distance of the ball's centre from the rim axis that still scores, in metres.</summary>
        public float ScoreRadius => _scoreRadius;

        /// <summary>
        /// Where the last counted basket crossed the rim plane, in world space. Diagnostic: comparing its
        /// distance from the axis against <see cref="ScoreRadius"/> is how you tell a clean drop from a
        /// ball that rattled in off the ring, and how you tune the radius without guessing.
        /// </summary>
        public Vector3 LastScoreCrossing { get; private set; }

        [Inject]
        private void Inject(IEventService events, ISoundService sounds)
        {
            _events = events;
            _sounds = sounds;
        }

        private void Awake()
        {
            // A non-trigger collider here would block the shot instead of measuring it.
            Collider col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
            {
                col.isTrigger = true;
                Debug.LogWarning($"{nameof(HoopScoreZone)} on '{name}': its collider was not a trigger and " +
                                 "has been switched to one — a solid volume inside the rim would stop the ball.", this);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            Rigidbody body = other.attachedRigidbody;
            if (body == null || body.GetComponent<ScoreableBall>() == null) return;

            // Seed the previous sample on entry, so the first FixedUpdate compares against a real
            // position rather than against Vector3.zero and reports a spurious crossing.
            _lastCentre[body] = body.worldCenterOfMass;
        }

        private void OnTriggerExit(Collider other)
        {
            Rigidbody body = other.attachedRigidbody;
            if (body == null) return;
            _lastCentre.Remove(body);
        }

        private void FixedUpdate()
        {
            if (_lastCentre.Count == 0) return;

            _tracked.Clear();
            foreach (Rigidbody body in _lastCentre.Keys) _tracked.Add(body);

            foreach (Rigidbody body in _tracked)
            {
                // A ball destroyed or pooled while inside the trigger never raises OnTriggerExit.
                if (body == null) { _lastCentre.Remove(body); continue; }

                Vector3 previous = _lastCentre[body];
                Vector3 current = body.worldCenterOfMass;

                if (CrossedDownThroughRim(body, previous, current, out Vector3 crossing))
                    RegisterBasket(body, crossing);

                _lastCentre[body] = current;
            }
        }

        /// <summary>
        /// True when the segment <paramref name="previous"/> → <paramref name="current"/> passes through
        /// the rim plane travelling downward, inside the ring. <paramref name="crossing"/> is the point on
        /// the plane where it did.
        /// </summary>
        private bool CrossedDownThroughRim(Rigidbody body, Vector3 previous, Vector3 current, out Vector3 crossing)
        {
            crossing = current;

            float rimY = RimCentre.y;
            // Strictly above then at-or-below: a ball sitting exactly on the plane cannot re-trigger.
            if (previous.y <= rimY || current.y > rimY) return false;

            if (_ignoreHeldBalls && body.isKinematic) return false;

            if (_nextScoreTime.TryGetValue(body, out float readyAt) && Time.time < readyAt) return false;

            // Where on the plane the crossing happened. The denominator cannot be zero: previous.y is
            // strictly greater than rimY and current.y is not, so they differ.
            float t = (previous.y - rimY) / (previous.y - current.y);
            crossing = Vector3.Lerp(previous, current, t);

            Vector3 centre = RimCentre;
            float dx = crossing.x - centre.x;
            float dz = crossing.z - centre.z;
            return dx * dx + dz * dz <= _scoreRadius * _scoreRadius;
        }

        private void RegisterBasket(Rigidbody body, Vector3 crossing)
        {
            _nextScoreTime[body] = Time.time + _rescoreCooldown;
            LastScoreCrossing = crossing;

            if (_netSounds != null && _netSounds.Length > 0)
                _sounds?.PlayRandomSoundFxAtPosition(_netSounds, transform, _netVolume);

            if (_events == null)
            {
                Debug.LogWarning($"{nameof(HoopScoreZone)} on '{name}': no IEventService injected — the " +
                                 "basket will not be counted. Does this scene have a ContainerScope?", this);
                return;
            }
            _events.Publish(new HoopScoredEvent(crossing, this));
        }

#if UNITY_EDITOR
        /// <summary>Draws the scoring ring so the zone can be lined up with the rim mesh by eye.</summary>
        private void OnDrawGizmosSelected()
        {
            Vector3 centre = RimCentre;
            Gizmos.color = new Color(0.40f, 0.87f, 0.91f, 0.9f);

            const int segments = 48;
            Vector3 previous = centre + new Vector3(_scoreRadius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 next = centre + new Vector3(Mathf.Cos(a) * _scoreRadius, 0f, Mathf.Sin(a) * _scoreRadius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }

            // The crossing direction that scores.
            Gizmos.DrawLine(centre + Vector3.up * 0.12f, centre - Vector3.up * 0.12f);
            Gizmos.DrawLine(centre - Vector3.up * 0.12f, centre + new Vector3(0.03f, -0.07f, 0f));
            Gizmos.DrawLine(centre - Vector3.up * 0.12f, centre + new Vector3(-0.03f, -0.07f, 0f));
        }
#endif
    }
}
