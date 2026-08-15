using System;
using System.Collections;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// One fish: patrols its patch of water, and swims to the hook when the bite schedule commissions it.
    ///
    /// Three things about this component are load-bearing and were each the cause of a shipped bug:
    ///
    /// <b>It moves in FixedUpdate, through the Rigidbody.</b> It used to write <c>transform.position</c>
    /// directly in <c>Update</c> on a non-kinematic, interpolated body. With <c>AutoSyncTransforms</c> off
    /// (this project's setting) the physics engine writes its own pose back over those writes, so the
    /// fish's effective speed depended on the ratio of render frames to physics steps — several times
    /// higher in the Editor than in a 72 Hz headset. That is most of why this worked on a desktop and not
    /// in a build.
    ///
    /// <b>Its step is clamped to the target.</b> Arrival used to be <c>distance &lt;= stoppingDistance</c>
    /// against an unclamped step. At the shipped 25 m/s that is a 5 mm window inside a 347 mm stride, and
    /// with a constant frame time the recurrence <c>d → |d − s|</c> settles into a stable two-cycle that
    /// straddles the hook and never enters the window at all. A VSync-locked headset holds that cycle
    /// forever; the Editor's jittery delta time shakes it loose within a few frames. <c>MoveTowards</c>
    /// removes the overshoot entirely, so arrival is reached, not gambled on.
    ///
    /// <b>Arrival calls back; it does not rely on a trigger.</b> The catch used to require a teleporting
    /// fish to overlap a 12.5 mm sphere. Now reaching the hook invokes the callback the schedule handed
    /// over, and the hooking is done in code. The trigger still works and is still wired — it is simply no
    /// longer the only way a fish can be caught.
    /// </summary>
    public class FishBehaviour : MonoBehaviour
    {
        [Header("References")]
        public FishArea fishArea;

        // NOTE: the fields below keep their original names on purpose, against this project's
        // _camelCase convention for serialized fields. They carry authored values in four scenes
        // (RasElBar, RasElBar_Demo, ToBuild_El_manara and a recovery scene); renaming one silently
        // drops that scene's override back to the prefab default. New fields follow the convention.
        [Header("Patrol")]
        public float patrolSpeed = 1.5f;
        public float patrolPointReachDistance = 0.5f;

        [Header("Hook Investigation")]
        [Tooltip("Upper bound on approach speed. The bite schedule decides the actual speed from how " +
                 "far the fish has to swim and how long it has left, so this is a ceiling, not a target.")]
        public float swimToHookSpeed = 3f;

        [Tooltip("Authored arrival distance. Raised to _minArrivalDistance at runtime if it is smaller — " +
                 "an arrival window tighter than one frame's movement can never be hit.")]
        public float stoppingDistance = 0.25f;

        [Range(0f, 1f)]
        public float biteChance = 1f;

        public float minReactionDelay = 0f;
        public float maxReactionDelay = 0f;

        [Header("Sanity Limits")]
        [Tooltip("Optional. Leave empty to use the project's FishingSettings asset from Resources; the " +
                 "values below are only the fallback for a scene that has neither.")]
        [SerializeField] private FishingSettingsSO _settings;

        [Tooltip("Fallback ceiling on patrol speed. RasElBar shipped a 25 m/s override on every fish, " +
                 "which was accidental — it scatters the shoal across the whole area within seconds " +
                 "of load. Clamped here rather than in the scene so no scene file has to be edited.")]
        [SerializeField] private float _maxPatrolSpeed = 2f;

        [Tooltip("Fallback ceiling applied to swimToHookSpeed for the same reason.")]
        [SerializeField] private float _maxApproachSpeed = 6f;

        [Tooltip("Fallback floor on the arrival distance, so arrival is always reachable in one step.")]
        [SerializeField] private float _minArrivalDistance = 0.12f;

        [Tooltip("Fallback for how far from where it spawned this fish will wander. The area box in " +
                 "RasElBar is 672 x 649 m and patrol targets used to be drawn from all of it, so fish " +
                 "dispersed hundreds of metres from the jetty and took a minute or more to come back.")]
        [SerializeField] private float _patrolRadius = 14f;

        private Rigidbody _fishRb;
        private Vector3 _homePosition;

        private Vector3 _patrolTarget;
        private bool _isInsideFishArea;

        private bool _movingToHook;
        private bool _isHooked;
        private Transform _hookTarget;
        private float _approachSpeed;
        private Action<FishBehaviour> _onArrived;

        private Coroutine _investigateCoroutine;

        /// <summary>True once this fish has taken a hook and not yet been released from it.</summary>
        public bool IsHooked => _isHooked;

        /// <summary>True while this fish is committed to a hook — swimming to it or waiting to start.</summary>
        public bool IsCommitted => _movingToHook || _investigateCoroutine != null;

        /// <summary>True while the fish is tracked inside its area and therefore able to act.</summary>
        public bool IsInsideFishArea => _isInsideFishArea;

        // Resolved through the shared asset when there is one, so the shoal is tuned in a single place.
        private float MaxPatrolSpeed => _settings != null ? _settings.MaxPatrolSpeed : _maxPatrolSpeed;
        private float MaxApproachSpeed => _settings != null ? _settings.MaxApproachSpeed : _maxApproachSpeed;
        private float PatrolRadius => _settings != null ? _settings.PatrolRadius : _patrolRadius;

        private float ArrivalDistance => Mathf.Max(
            stoppingDistance,
            _settings != null ? _settings.ArrivalDistance : _minArrivalDistance);

        private void Awake()
        {
            if (_settings == null) _settings = FishingSettingsSO.Load();

            _fishRb = GetComponent<Rigidbody>();
            _homePosition = transform.position;
        }

        private void Start()
        {
            if (fishArea == null)
            {
                fishArea = FindFirstObjectByType<FishArea>();
            }

            if (fishArea == null)
            {
                // Used to be an unguarded call straight into RegisterFish, which threw here and took the
                // rest of Start with it — including the patrol routine. A fish with no area should just
                // sit there, not break its own initialisation.
                Debug.LogWarning($"Fish '{name}' found no FishArea in the scene — it will not patrol or " +
                                 "respond to a hook. Add a FishArea, or assign one on this component.", this);
                return;
            }

            fishArea.RegisterFish(this);

            // The area's trigger only fires for a fish that CROSSES the boundary. A fish authored already
            // inside it never crosses, and with gravity off it never moves on its own either — so it
            // could sit there permanently with isInsideFishArea false, refusing to act while looking
            // perfectly healthy. Seed the state from a bounds test instead of waiting for physics.
            SetInsideFishArea(fishArea.Contains(transform.position));

            PickNewPatrolTarget();
            StartCoroutine(PatrolTargetRoutine());
        }

        private void OnDestroy()
        {
            if (fishArea != null)
            {
                fishArea.UnregisterFish(this);
            }
        }

        private void FixedUpdate()
        {
            if (!_isInsideFishArea) return;
            if (_isHooked) return;

            if (_movingToHook)
            {
                SwimTowardsHook();
            }
            else
            {
                Patrol();
            }
        }

        private void Patrol()
        {
            Vector3 toTarget = _patrolTarget - transform.position;

            if (toTarget.magnitude <= patrolPointReachDistance)
            {
                PickNewPatrolTarget();
                return;
            }

            float speed = Mathf.Min(patrolSpeed, MaxPatrolSpeed);
            MoveTo(Vector3.MoveTowards(transform.position, _patrolTarget, speed * Time.fixedDeltaTime));
            RotateTowards(toTarget);
        }

        private void SwimTowardsHook()
        {
            if (_hookTarget == null)
            {
                // The hook was reeled in or destroyed mid-approach. Go back to patrolling rather than
                // freezing — this fish must stay available for the next cast.
                StopApproach();
                return;
            }

            Vector3 toHook = _hookTarget.position - transform.position;

            if (toHook.magnitude <= ArrivalDistance)
            {
                Arrive();
                return;
            }

            MoveTo(Vector3.MoveTowards(
                transform.position,
                _hookTarget.position,
                _approachSpeed * Time.fixedDeltaTime));

            RotateTowards(toHook);
        }

        private void MoveTo(Vector3 position)
        {
            if (_fishRb != null && _fishRb.isKinematic)
            {
                // MovePosition on a kinematic body is swept and interpolated by the physics engine, so
                // trigger callbacks along the way still fire and the motion reads smoothly between
                // physics steps. A raw transform write does neither.
                _fishRb.MovePosition(position);
            }
            else
            {
                transform.position = position;
            }
        }

        private void RotateTowards(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.001f) return;

            Quaternion target = Quaternion.LookRotation(direction);

            // Exponential smoothing rather than `5f * deltaTime`: the naive form turns faster the higher
            // the frame rate, which is one more way the Editor and the headset disagreed.
            float t = 1f - Mathf.Exp(-5f * Time.fixedDeltaTime);

            Quaternion rotation = Quaternion.Slerp(transform.rotation, target, t);

            if (_fishRb != null && _fishRb.isKinematic)
            {
                _fishRb.MoveRotation(rotation);
            }
            else
            {
                transform.rotation = rotation;
            }
        }

        private IEnumerator PatrolTargetRoutine()
        {
            while (true)
            {
                if (!_movingToHook && !_isHooked)
                {
                    PickNewPatrolTarget();
                }

                yield return new WaitForSeconds(UnityEngine.Random.Range(3f, 8f));
            }
        }

        private void PickNewPatrolTarget()
        {
            _patrolTarget = fishArea != null
                ? fishArea.GetRandomPointNear(_homePosition, PatrolRadius)
                : _homePosition;
        }

        /// <summary>
        /// Commission this fish to take the hook. <paramref name="onArrived"/> fires once it gets there —
        /// the schedule uses that to do the actual hooking, so the catch no longer depends on a physics
        /// trigger overlap that a fast fish can step straight over.
        /// </summary>
        public bool BeginApproach(Transform hook, float speed, Action<FishBehaviour> onArrived)
        {
            if (hook == null) return false;
            if (_isHooked) return false;
            if (!_isInsideFishArea) return false;

            _onArrived = onArrived;
            SetApproachSpeed(speed);

            if (_movingToHook)
            {
                // Already on its way — just retarget. Re-rolling the bite chance here would let a fish
                // that already agreed to come change its mind, which is exactly the kind of silent
                // reversal that made this system feel broken.
                _hookTarget = hook;
                return true;
            }

            if (_investigateCoroutine != null) return true;

            _hookTarget = hook;
            _investigateCoroutine = StartCoroutine(BeginApproachAfterDelay());
            return true;
        }

        /// <summary>Update the approach speed mid-swim. The schedule recomputes this every tick.</summary>
        public void SetApproachSpeed(float speed)
        {
            _approachSpeed = Mathf.Clamp(speed, 0.1f, Mathf.Min(swimToHookSpeed, MaxApproachSpeed));
        }

        private IEnumerator BeginApproachAfterDelay()
        {
            float delay = UnityEngine.Random.Range(minReactionDelay, maxReactionDelay);

            if (delay > 0f) yield return new WaitForSeconds(delay);

            _movingToHook = true;
            _investigateCoroutine = null;
        }

        /// <summary>
        /// Put this fish on the hook immediately, wherever it currently is. The schedule's last resort
        /// when the promised window is about to expire — it is what turns "usually bites" into "always
        /// bites within the window".
        /// </summary>
        public void ForceArrive()
        {
            if (_isHooked) return;
            if (_hookTarget == null) return;

            MoveTo(_hookTarget.position);
            Arrive();
        }

        private void Arrive()
        {
            _movingToHook = false;
            _isHooked = true;

            // Order matters: the body has to be dynamic again BEFORE anything tries to joint it, because
            // a FixedJoint on a kinematic body has nothing to pull against.
            UpdateBodyMode();

            Action<FishBehaviour> callback = _onArrived;
            _onArrived = null;
            callback?.Invoke(this);
        }

        /// <summary>
        /// Legacy entry point kept so any Inspector-wired or older caller still works. Prefer
        /// <see cref="BeginApproach"/>, which does not gamble on <see cref="biteChance"/>.
        /// </summary>
        public void InvestigateHook(Transform hook)
        {
            if (_movingToHook || _isHooked) return;
            if (UnityEngine.Random.value > biteChance) return;

            BeginApproach(hook, swimToHookSpeed, null);
        }

        /// <summary>
        /// Stop pursuing the hook — it left the water before this fish got there. Does nothing once the
        /// fish is actually hooked.
        /// </summary>
        public void CancelHookInvestigation()
        {
            if (_isHooked) return;
            StopApproach();
        }

        private void StopApproach()
        {
            if (_investigateCoroutine != null)
            {
                StopCoroutine(_investigateCoroutine);
                _investigateCoroutine = null;
            }

            _movingToHook = false;
            _hookTarget = null;
            _onArrived = null;
        }

        /// <summary>
        /// Take this fish off the hook and return it to the shoal.
        ///
        /// Nothing used to call this because nothing could: <c>isHooked</c> was set true in one place and
        /// assigned false nowhere in the codebase. Every fish that was ever hooked — including one the
        /// player grabbed and then dropped — stopped patrolling forever, refused every future
        /// invitation, sank under gravity and was pruned from the area. The shoal could only shrink.
        /// </summary>
        public void ReleaseFromHook()
        {
            if (!_isHooked) return;

            _isHooked = false;
            _hookTarget = null;
            _onArrived = null;
            _movingToHook = false;

            _homePosition = transform.position;
            PickNewPatrolTarget();

            UpdateBodyMode();
        }

        public void SetInsideFishArea(bool inside)
        {
            if (_isInsideFishArea == inside) return;

            _isInsideFishArea = inside;
            UpdateBodyMode();
        }

        /// <summary>
        /// Central authority for this fish's physics mode.
        /// - Swimming (inside the area, not hooked): kinematic, no gravity. The fish is script-driven, so
        ///   letting PhysX integrate it as well only produces a fight neither side wins.
        /// - Hooked, or outside the area: dynamic with gravity, so the joint and the line can act on it.
        /// </summary>
        private void UpdateBodyMode()
        {
            if (_fishRb == null) return;

            bool swimming = _isInsideFishArea && !_isHooked;

            _fishRb.isKinematic = swimming;
            _fishRb.useGravity = !swimming;

            if (!swimming)
            {
                // A body that has been kinematic can come back dynamic asleep, and a sleeping body
                // generates no trigger callbacks at all — which is how a fish could arrive at the hook
                // and simply never be noticed by it.
                _fishRb.WakeUp();
            }
        }
    }
}
