using UnityEngine;
using UnityEngine.Serialization;

namespace DumyatRuntime
{
    /// <summary>
    /// Notices the player coming within this merchant's detection range and gets a negotiation started.
    /// Sits on the merchant GameObject next to <see cref="MerchantNPC"/>. Detection is a plain DISTANCE
    /// check (measured every frame from the merchant to the player) — no trigger collider is involved, so
    /// the range is easy to tune and visualise (see the gizmos) and doesn't depend on collider overlap.
    ///
    /// With a <see cref="MerchantApproachMover"/> on the same GameObject the MERCHANT walks over to the
    /// player and the negotiation starts on arrival; without one it starts immediately in place (legacy
    /// behaviour). Either way the request routes through
    /// <see cref="MerchantApproachCoordinator.PlayerInitiatesWithMerchant"/> so the coordinator's
    /// one-at-a-time lock and fish check still apply.
    ///
    /// ONE CONVERSATION PER VISIT: the merchant engages the player exactly once per time they are in
    /// range. After a negotiation ends, it will NOT start another while the player stays in range — the
    /// player must LEAVE the range and come back to talk again. While in range it keeps RE-TRYING the
    /// single engagement until it actually starts (e.g. the coordinator/mover was momentarily busy); it
    /// never starts a second one.
    ///
    /// WALK-AWAY CANCEL: while THIS merchant is mid-negotiation, moving beyond the detection range for
    /// longer than <c>_exitGraceSeconds</c> cancels the sale (the merchant walks back to its spot). The
    /// grace window absorbs momentary boundary/VR jitter; coming back in range before it elapses keeps the
    /// conversation alive. Detection range is the SINGLE range — engagement and cancellation share it.
    /// </summary>
    [RequireComponent(typeof(MerchantNPC))]
    public class MerchantProximityTrigger : MonoBehaviour
    {
        [Tooltip("Tag identifying the player object.")]
        [SerializeField] private string _playerTag = "Player";

        [Tooltip("Coordinator that owns the negotiation lock. Auto-resolved from the scene when empty.")]
        [SerializeField, FormerlySerializedAs("_spawner")] private MerchantApproachCoordinator _coordinator;

        [Tooltip("Legacy mode only (no MerchantApproachMover): rotate the merchant to face the " +
                 "player when they come into range.")]
        [SerializeField] private bool _facePlayerOnApproach = true;

        [Tooltip("Legacy mode only: fastest he may turn, in degrees per second — a cap on the ease below.")]
        [SerializeField, Min(10f)] private float _turnSpeed = 220f;

        [Tooltip("Legacy mode only: how eagerly he eases round to face the player. Higher is snappier.")]
        [SerializeField, Min(0.5f)] private float _faceResponsiveness = 6f;

        [Header("Detection Range")]
        [Tooltip("Horizontal distance (metres) at which the merchant notices the player and engages, and " +
                 "— once negotiating — beyond which the sale is cancelled. Make it comfortably larger than " +
                 "where the player stands to use the sell scale, so reaching the scale never ends the sale.")]
        [SerializeField, Min(0.5f)] private float _detectionRange = 4f;

        [Tooltip("Seconds the player must stay BEYOND the detection range before an in-progress " +
                 "negotiation is cancelled. A small value absorbs momentary boundary/VR jitter; 0 cancels " +
                 "instantly.")]
        [SerializeField, Min(0f)] private float _exitGraceSeconds = 0.75f;

        [Header("Gizmos")]
        [Tooltip("Draw the detection range and a live line to the player in the Scene view.")]
        [SerializeField] private bool _drawGizmos = true;

        [Tooltip("Height off the merchant's base to draw the range ring / distance line at, for clarity.")]
        [SerializeField] private float _gizmoHeight = 1f;

        private readonly YawTurner _turner = new YawTurner();

        // World point the merchant is easing round to face, or null when he is already facing it.
        private Vector3? _faceTarget;

        private MerchantNPC _merchant;
        private MerchantApproachMover _mover;
        private Transform _player;

        // True once the merchant has engaged the player during the CURRENT time-in-range. Blocks any
        // further engagement until the player leaves the range and comes back.
        private bool _engagedThisVisit;

        // Seconds the player has continuously been beyond the detection range while this merchant negotiates.
        private float _outOfRangeTimer;

        private void Awake()
        {
            _merchant = GetComponent<MerchantNPC>();
            _mover = GetComponent<MerchantApproachMover>();

            if (_coordinator == null) _coordinator = FindFirstObjectByType<MerchantApproachCoordinator>();
            if (_coordinator == null)
                Debug.LogWarning($"MerchantProximityTrigger: no MerchantApproachCoordinator in scene for '{name}'; " +
                                 "approaches will do nothing.");
        }

        private void Update()
        {
            // Before the early-outs below, so a turn already under way still completes.
            UpdateFacing();

            if (_merchant == null || _coordinator == null) return;

            Transform player = ResolvePlayer();
            if (player == null) return;

            if (HorizontalDistanceTo(player) <= _detectionRange)
            {
                // In range: keep retrying the single engagement until it starts (coordinator/mover may be busy).
                _outOfRangeTimer = 0f;
                if (!_engagedThisVisit) TryEngage(player);
                return;
            }

            // Out of range: re-arm the once-per-visit engagement for a later return.
            _engagedThisVisit = false;

            // While THIS merchant is mid-sale, staying out of range past the grace window cancels it.
            if (_coordinator.ActiveMerchant == _merchant)
            {
                _outOfRangeTimer += Time.deltaTime;
                if (_outOfRangeTimer >= _exitGraceSeconds)
                {
                    _outOfRangeTimer = 0f;
                    Debug.Log($"MerchantProximityTrigger: player left '{name}' detection range " +
                              $"({HorizontalDistanceTo(player):0.#}m > {_detectionRange:0.#}m) — cancelling negotiation.");
                    _coordinator.PlayerLeftConversationArea(_merchant);
                }
            }
            else
            {
                _outOfRangeTimer = 0f;
            }
        }

        private void TryEngage(Transform player)
        {
            if (_coordinator == null || _merchant == null || player == null) return;
            if (_engagedThisVisit) return;                 // already had this visit's one conversation
            // One NPC at a time: block while any merchant is negotiating OR already walking to/from the
            // player. Checking the coordinator-wide state (not just this merchant's mover) is what stops
            // two merchants from approaching together. Update retries once the coast is clear.
            if (_coordinator.IsAnyMerchantEngaging()) return;

            // Mark engaged BEFORE starting so nothing can slip a second conversation in this visit.
            _engagedThisVisit = true;

            if (_mover != null)
            {
                // The merchant walks to the player; the negotiation is initiated on arrival.
                Debug.Log("Approaching player: " + player.name);
                _mover.ApproachAndInitiate(player);
            }
            else
            {
                // Legacy in-place start.
                if (_facePlayerOnApproach) FaceToward(player.position);
                _coordinator.PlayerInitiatesWithMerchant(_merchant);
            }
        }

        // Finds and caches the player transform by tag.
        private Transform ResolvePlayer()
        {
            if (_player != null) return _player;
            GameObject go = GameObject.FindWithTag(_playerTag);
            if (go != null) _player = go.transform;
            return _player;
        }

        // Horizontal (ground-plane) distance so VR head height never affects range.
        private float HorizontalDistanceTo(Transform player)
        {
            Vector3 d = player.position - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        // Asks for a smooth turn toward the point; the ease itself happens in Update via the shared
        // YawTurner, so the merchant swings round to greet the player instead of clicking to face them.
        // Rotates only the visual body (not the root), so siblings like the sell scale don't turn.
        private void FaceToward(Vector3 worldPoint) => _faceTarget = worldPoint;

        // Legacy path only (no MerchantApproachMover): the mover owns facing when one is present.
        private void UpdateFacing()
        {
            if (!_faceTarget.HasValue) return;

            Transform facing = _merchant != null ? _merchant.CharacterRoot : transform;
            if (_turner.Step(facing, _faceTarget.Value, _faceResponsiveness, _turnSpeed, Time.deltaTime))
                _faceTarget = null;
        }

        // Scene-view visualisation: the detection range ring plus a live line to the player showing the
        // current distance (green = in range, red = out). Editor-only; stripped from player builds.
        private void OnDrawGizmos()
        {
            if (!_drawGizmos) return;

            Vector3 center = transform.position + Vector3.up * _gizmoHeight;
            Transform player = Application.isPlaying
                ? _player
                : (string.IsNullOrEmpty(_playerTag) ? null : GameObject.FindWithTag(_playerTag)?.transform);

            bool inRange = player != null && HorizontalDistanceTo(player) <= _detectionRange;

            Gizmos.color = inRange ? Color.green : new Color(1f, 0.55f, 0f); // green in range, orange otherwise
            Gizmos.DrawWireSphere(center, _detectionRange);

            if (player == null) return;

            Vector3 p = player.position;
            p.y = center.y;   // draw the line on the same plane as the ring
            Gizmos.color = inRange ? Color.green : Color.red;
            Gizmos.DrawLine(center, p);
            Gizmos.DrawWireSphere(p, 0.1f);
#if UNITY_EDITOR
            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.Label((center + p) * 0.5f + Vector3.up * 0.15f,
                $"{HorizontalDistanceTo(player):0.00} m / {_detectionRange:0.0} m");
#endif
        }
    }
}
