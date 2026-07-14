using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Walks the merchant to the player and back. When <see cref="ApproachAndInitiate"/> is called
    /// (by <see cref="MerchantProximityTrigger"/> when the player comes into detection range), the
    /// merchant walks straight to the player, stops at conversation distance, faces them and starts
    /// the negotiation through <see cref="MerchantSpawner.PlayerInitiatesWithMerchant"/> — so the
    /// one-at-a-time lock and fish check still apply. When the negotiation ends (any outcome), the
    /// merchant walks back to the position/rotation it had at startup and idles there.
    ///
    /// Locomotion is a simple straight-line kinematic move with the Animator cross-faded between
    /// the Walk and Idle states — no NavMesh, which is fine for the open test market. The greeting
    /// Wave on arrival stays <see cref="MerchantAnimationDriver"/>'s job.
    /// </summary>
    [RequireComponent(typeof(MerchantNPC))]
    public class MerchantApproachMover : MonoBehaviour
    {
        private enum State { AtHome, WalkingToPlayer, Engaged, WalkingHome }

        [Header("Locomotion")]
        [Tooltip("Walk speed in metres per second.")]
        [SerializeField, Min(0.1f)] private float _walkSpeed = 1.4f;

        [Tooltip("Turn speed while walking, in degrees per second.")]
        [SerializeField, Min(10f)] private float _turnSpeed = 360f;

        [Tooltip("Distance from the player at which the merchant stops and starts the conversation.")]
        [SerializeField, Min(0.5f)] private float _stopDistance = 1.7f;

        [Tooltip("How close to the home position counts as arrived when walking back.")]
        [SerializeField, Min(0.01f)] private float _homeArriveDistance = 0.08f;

        [Header("Animator States")]
        [Tooltip("Animator to drive. Auto-resolved from this GameObject or its children when empty.")]
        [SerializeField] private Animator _animator;

        [Tooltip("Name of the looping walk state in the controller.")]
        [SerializeField] private string _walkState = "Walk";

        [Tooltip("Name of the looping idle state in the controller.")]
        [SerializeField] private string _idleState = "Idle";

        [Header("Wiring")]
        [Tooltip("Spawner that owns the negotiation lock. Auto-resolved from the scene when empty.")]
        [SerializeField] private MerchantSpawner _spawner;

        private MerchantNPC _merchant;
        private Transform _playerTarget;
        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private State _state = State.AtHome;

        /// <summary>True while walking or mid-conversation — i.e. not idling at its home spot.</summary>
        public bool IsBusy => _state != State.AtHome;

        private void Awake()
        {
            _merchant = GetComponent<MerchantNPC>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_spawner == null) _spawner = FindFirstObjectByType<MerchantSpawner>();

            _homePosition = transform.position;
            _homeRotation = transform.rotation;
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
        /// through the spawner. Ignored while already walking or engaged.
        /// </summary>
        public void ApproachAndInitiate(Transform player)
        {
            if (_state != State.AtHome || player == null) return;

            _playerTarget = player;
            _state = State.WalkingToPlayer;
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
                FaceToward(_playerTarget.position);
                _state = State.Engaged;
                EnsureAnimatorState(_idleState);

                // Route through the spawner (lock + fish check). If it rejects, no negotiation
                // starts and OnNegotiationEnded never fires — verify shortly after and go home.
                if (_spawner != null) _spawner.PlayerInitiatesWithMerchant(_merchant);
                CancelInvoke(nameof(VerifyEngaged));
                Invoke(nameof(VerifyEngaged), 0.25f);
                return;
            }

            MoveAlong(toPlayer);
        }

        private void UpdateWalkHome()
        {
            Vector3 toHome = _homePosition - transform.position;
            toHome.y = 0f;

            if (toHome.magnitude <= _homeArriveDistance)
            {
                transform.position = new Vector3(_homePosition.x, transform.position.y, _homePosition.z);
                transform.rotation = _homeRotation;
                _state = State.AtHome;
                EnsureAnimatorState(_idleState);
                return;
            }

            MoveAlong(toHome);
        }

        // Straight-line kinematic step with a turn toward the travel direction. The walk state is
        // re-asserted each frame so another negotiation-end listener (e.g. the animation driver's
        // return-to-idle) can't leave the merchant sliding in its idle pose.
        private void MoveAlong(Vector3 flatDirection)
        {
            Vector3 direction = flatDirection.normalized;
            Quaternion look = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _turnSpeed * Time.deltaTime);
            transform.position += direction * (_walkSpeed * Time.deltaTime);
            EnsureAnimatorState(_walkState);
        }

        // Invoke target: the spawner rejected the approach (busy / no fish) if this merchant never
        // became the active one — walk back instead of standing at the player forever.
        private void VerifyEngaged()
        {
            if (_state != State.Engaged) return;
            if (_spawner == null || _spawner.ActiveMerchant != _merchant)
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
                transform.rotation = _homeRotation;
                _state = State.AtHome;
                EnsureAnimatorState(_idleState);
                return;
            }

            _state = State.WalkingHome;
        }

        private void FaceToward(Vector3 worldPoint)
        {
            Vector3 flat = worldPoint - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
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
