using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.AI;

namespace DumyatRuntime
{
    /// <summary>
    /// Walks the merchant to the player and back. When <see cref="ApproachAndInitiate"/> is called
    /// (by <see cref="MerchantProximityTrigger"/> when the player comes into detection range), the
    /// merchant walks to the player, stops at conversation distance, faces them and starts the
    /// negotiation through <see cref="MerchantApproachCoordinator.PlayerInitiatesWithMerchant"/> — so the
    /// one-at-a-time lock and fish check still apply. When the negotiation ends (any outcome), the
    /// merchant walks back to the position/rotation it had at startup and idles there.
    ///
    /// Locomotion is a <see cref="NavMeshAgent"/>: the merchant PATHFINDS around stalls, props and
    /// walls (the scene needs a baked NavMesh — see the NavMeshSurface in the scene). If no NavMesh
    /// is available at runtime the mover degrades gracefully to a straight-line walk so the merchant
    /// still functions. The Animator is cross-faded between the Walk and Idle states; the greeting
    /// Wave on arrival stays <see cref="MerchantAnimationDriver"/>'s job.
    ///
    /// With walking turned OFF the agent is DISABLED outright rather than merely left unused: an enabled
    /// agent owns the transform's HEIGHT (it snaps to the baked surface and pins it there), which silently
    /// lifted stationary merchants off the ground wherever the NavMesh was carved out from under them.
    /// See ConfigureAgent.
    /// </summary>
    [RequireComponent(typeof(MerchantNPC))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class MerchantApproachMover : MonoBehaviour
    {
        private enum State { AtHome, WalkingToPlayer, Engaged, WalkingHome }

        [Header("Locomotion")]
        [Tooltip("When ON, the merchant WALKS to the player and the conversation starts on arrival. " +
                 "When OFF, the merchant stays put and engages IN PLACE as soon as the player is in " +
                 "range — no walking, and no NavMesh needed.")]
        [SerializeField] private bool _walkToPlayer = true;

        [Tooltip("Walk speed in metres per second.")]
        [SerializeField, Min(0.1f)] private float _walkSpeed = 1.4f;

        [Tooltip("Turn speed while walking, in degrees per second.")]
        [SerializeField, Min(10f)] private float _turnSpeed = 360f;

        [Tooltip("Distance from the player at which the merchant stops and starts the conversation.")]
        [SerializeField, Min(0.5f)] private float _stopDistance = 1.7f;

        [Tooltip("How close to the home position counts as arrived when walking back.")]
        [SerializeField, Min(0.01f)] private float _homeArriveDistance = 0.2f;

        [Header("Animator States")]
        [Tooltip("Animator to drive. Auto-resolved from this GameObject or its children when empty.")]
        [SerializeField] private Animator _animator;

        [Tooltip("Name of the looping walk state in the controller.")]
        [SerializeField] private string _walkState = "Walk";

        [Tooltip("Name of the looping idle state in the controller.")]
        [SerializeField] private string _idleState = "Idle";

        [Header("Wiring")]
        [Tooltip("Coordinator that owns the negotiation lock. Auto-resolved from the scene when empty.")]
        [SerializeField, FormerlySerializedAs("_spawner")] private MerchantApproachCoordinator _coordinator;

        private MerchantNPC _merchant;
        private NavMeshAgent _agent;
        private Transform _playerTarget;
        private Transform _facing;          // the visual body we rotate (not the root), so the sell scale stays put
        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private State _state = State.AtHome;

        /// <summary>True while walking or mid-conversation — i.e. not idling at its home spot.</summary>
        public bool IsBusy => _state != State.AtHome;

        private void Awake()
        {
            _merchant = GetComponent<MerchantNPC>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_coordinator == null) _coordinator = FindFirstObjectByType<MerchantApproachCoordinator>();

            // Rotate only the merchant's visual body, so siblings (the sell scale) don't turn with him.
            _facing = _merchant != null ? _merchant.CharacterRoot : transform;

            _homePosition = transform.position;
            _homeRotation = _facing.rotation;

            ConfigureAgent();
            EnsureBodyCollider();
        }

        // Set up the NavMeshAgent from the serialized locomotion tuning. Arrival is judged explicitly
        // against the live target (below), so the agent's own stoppingDistance is left at 0. If the
        // agent isn't sitting on the baked NavMesh yet, snap it onto the nearest point.
        private void ConfigureAgent()
        {
            _agent = GetComponent<NavMeshAgent>();
            if (_agent == null) _agent = gameObject.AddComponent<NavMeshAgent>();

            // A merchant that never walks has no use for the agent — and leaving it enabled COSTS him his
            // authored height: the agent snaps to the nearest baked NavMesh point and then pins the
            // transform at that surface every frame (updatePosition). Where the NavMesh is carved out from
            // under a stall, the nearest point is a raised ledge beside it, so the merchant stands in the
            // air (measured: +0.44 m on one, +0.11 m on another, while merchants with NavMesh directly
            // beneath them were fine). Disabling it keeps the transform exactly as placed.
            if (!_walkToPlayer)
            {
                _agent.enabled = false;
                return;
            }

            _agent.speed = _walkSpeed;
            _agent.angularSpeed = _turnSpeed;
            _agent.acceleration = 8f;
            _agent.stoppingDistance = 0f;
            _agent.autoBraking = true;
            // We rotate the visual body ourselves (see FaceDirection) so the root — and the sell scale
            // hanging off it — never yaws with the agent. The agent still drives POSITION.
            _agent.updateRotation = false;
            _agent.updateUpAxis = true;

            if (!_agent.isOnNavMesh &&
                NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                // Pathfinding REQUIRES the agent to be on the mesh, so the snap stands — but a large one
                // (especially vertical) means this merchant was placed off the baked NavMesh, and the
                // silent lift is exactly what reads in-game as "the NPC floats". Say so instead.
                Vector3 delta = hit.position - transform.position;
                if (Mathf.Abs(delta.y) > 0.05f || delta.magnitude > 0.5f)
                    Debug.LogWarning($"MerchantApproachMover: '{name}' is not on the baked NavMesh — " +
                                     $"snapping {delta.magnitude:0.##} m (Δy {delta.y:0.###}) to the " +
                                     "nearest point, which will change his height. Re-bake the NavMesh so " +
                                     "it covers this spot at floor level, or move him onto valid NavMesh.",
                                     this);
                _agent.Warp(hit.position);
            }
        }

        // A solid (non-trigger) collider so the physics-driven player can't walk THROUGH the merchant.
        // The prefab's own colliders are all triggers (detection range), which never block anything.
        private void EnsureBodyCollider()
        {
            foreach (var col in GetComponents<Collider>())
            {
                if (!col.isTrigger) return;
            }
            var capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.isTrigger = false;
            capsule.radius = 0.3f;
            capsule.height = 1.8f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
        }

        private void OnEnable()
        {
            if (_merchant != null) _merchant.OnNegotiationEnded += HandleNegotiationEnded;
        }

        private void OnDisable()
        {
            if (_merchant != null) _merchant.OnNegotiationEnded -= HandleNegotiationEnded;
        }

        /// <summary>
        /// Starts walking to <paramref name="player"/>; on arrival the negotiation is initiated
        /// through the coordinator. Ignored while already walking or engaged.
        /// </summary>
        public void ApproachAndInitiate(Transform player)
        {
            if (_state != State.AtHome || player == null) return;

            _playerTarget = player;

            // Walking disabled: skip the walk and start the conversation where the merchant stands.
            if (!_walkToPlayer)
            {
                EngageInPlace();
                return;
            }

            _state = State.WalkingToPlayer;
            DriveAgentTo(player.position);
        }

        private void Update()
        {
            switch (_state)
            {
                case State.WalkingToPlayer:
                    UpdateWalkToPlayer();
                    break;
                case State.WalkingHome:
                    UpdateWalkHome();
                    break;
            }
        }

        private void UpdateWalkToPlayer()
        {
            if (_playerTarget == null)
            {
                StartWalkingHome();
                return;
            }

            Vector3 toPlayer = _playerTarget.position - transform.position;
            toPlayer.y = 0f;

            if (toPlayer.magnitude <= _stopDistance)
            {
                EngageInPlace();
                return;
            }

            // Keep re-targeting so the merchant tracks the (possibly moving) player.
            DriveAgentTo(_playerTarget.position, toPlayer);
            FaceTravel();
            EnsureAnimatorState(_walkState);
        }

        // Stop, face the player, and hand off to the coordinator (its one-at-a-time lock + fish check
        // still apply). Used both on ARRIVAL when walking, and IMMEDIATELY when walking is disabled. If
        // the coordinator rejects the approach, OnNegotiationEnded never fires — VerifyEngaged catches
        // that shortly after and walks/settles the merchant back home.
        private void EngageInPlace()
        {
            StopAgent();
            if (_playerTarget != null) FaceToward(_playerTarget.position);
            _state = State.Engaged;
            EnsureAnimatorState(_idleState);

            if (_coordinator != null) _coordinator.PlayerInitiatesWithMerchant(_merchant);
            CancelInvoke(nameof(VerifyEngaged));
            Invoke(nameof(VerifyEngaged), 0.25f);
        }

        private void UpdateWalkHome()
        {
            Vector3 toHome = _homePosition - transform.position;
            toHome.y = 0f;

            if (HasArrivedHome(toHome))
            {
                StopAgent();
                _facing.rotation = _homeRotation;
                _state = State.AtHome;
                EnsureAnimatorState(_idleState);
                return;
            }

            DriveAgentTo(_homePosition, toHome);
            FaceTravel();
            EnsureAnimatorState(_walkState);
        }

        // Home is reached when the merchant is within the arrival radius, or the agent has run its
        // path out and effectively stopped (covers it braking a hair short of the exact point).
        private bool HasArrivedHome(Vector3 flatToHome)
        {
            if (flatToHome.magnitude <= _homeArriveDistance) return true;
            return UseAgent() && !_agent.pathPending &&
                   _agent.remainingDistance <= 0.3f && _agent.velocity.sqrMagnitude < 0.04f;
        }

        // Send the agent toward a world point. Falls back to a straight-line transform move when no
        // NavMesh is available, so the merchant still walks (just without obstacle avoidance).
        private void DriveAgentTo(Vector3 worldPoint, Vector3 fallbackFlatDir = default)
        {
            if (UseAgent())
            {
                _agent.isStopped = false;
                _agent.SetDestination(worldPoint);
            }
            else
            {
                MoveStraightLine(fallbackFlatDir);
            }
        }

        private void StopAgent()
        {
            if (UseAgent())
            {
                _agent.isStopped = true;
                _agent.ResetPath();
            }
        }

        private bool UseAgent() => _agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh;

        // Graceful degradation only: straight-line step toward the flat direction with a turn. Moves the
        // root (position) but only turns the visual body, so the sell scale rides along without spinning.
        private void MoveStraightLine(Vector3 flatDirection)
        {
            if (flatDirection.sqrMagnitude < 0.0001f) return;
            Vector3 direction = flatDirection.normalized;
            FaceDirection(direction);
            transform.position += direction * (_walkSpeed * Time.deltaTime);
        }

        // Turn the merchant's visual body (never the root) toward the agent's current travel direction.
        // Called each walking frame because the NavMeshAgent's own rotation is disabled (ConfigureAgent).
        private void FaceTravel()
        {
            if (!UseAgent()) return;   // the straight-line fallback already faces in MoveStraightLine
            Vector3 v = _agent.velocity;
            v.y = 0f;
            FaceDirection(v);
        }

        // Rotates ONLY the visual body toward a flat world direction, smoothly by default or instantly
        // (e.g. snapping to face the player on arrival). The root stays put so siblings don't rotate.
        private void FaceDirection(Vector3 flatDirection, bool instant = false)
        {
            if (_facing == null || flatDirection.sqrMagnitude < 0.0001f) return;
            Quaternion look = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);
            _facing.rotation = instant
                ? look
                : Quaternion.RotateTowards(_facing.rotation, look, _turnSpeed * Time.deltaTime);
        }

        // Invoke target: the coordinator rejected the approach (busy / no fish) if this merchant never
        // became the active one — walk back instead of standing at the player forever.
        private void VerifyEngaged()
        {
            if (_state != State.Engaged) return;
            if (_coordinator == null || _coordinator.ActiveMerchant != _merchant)
            {
                Debug.Log($"MerchantApproachMover: '{name}' approach was rejected; returning home.");
                StartWalkingHome();
            }
        }

        private void HandleNegotiationEnded(MerchantNPC merchant, NegotiationPhase outcome)
        {
            if (_state == State.Engaged) StartWalkingHome();
        }

        private void StartWalkingHome()
        {
            _playerTarget = null;

            Vector3 toHome = _homePosition - transform.position;
            toHome.y = 0f;
            if (toHome.magnitude <= _homeArriveDistance)
            {
                StopAgent();
                _facing.rotation = _homeRotation;
                _state = State.AtHome;
                EnsureAnimatorState(_idleState);
                return;
            }

            _state = State.WalkingHome;
            DriveAgentTo(_homePosition, toHome);
        }

        private void FaceToward(Vector3 worldPoint)
        {
            Vector3 flat = worldPoint - transform.position;
            flat.y = 0f;
            FaceDirection(flat, instant: true);
        }

        // Cross-fades to the named state unless the animator is already in (or blending to) it.
        private void EnsureAnimatorState(string stateName)
        {
            if (_animator == null || string.IsNullOrEmpty(stateName)) return;

            int hash = Animator.StringToHash(stateName);
            if (!_animator.HasState(0, hash)) return;
            if (_animator.GetCurrentAnimatorStateInfo(0).shortNameHash == hash) return;
            if (_animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).shortNameHash == hash) return;

            _animator.CrossFadeInFixedTime(hash, 0.15f);
        }
    }
}
