using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Detects the player entering this merchant's detection radius and gets a negotiation
    /// started. Sits on the merchant GameObject next to <see cref="MerchantNPC"/> with a trigger
    /// collider sized to the DETECTION range (how far away the merchant notices a potential
    /// customer).
    ///
    /// With a <see cref="MerchantApproachMover"/> on the same GameObject the MERCHANT walks over
    /// to the player and the negotiation starts on arrival; without one it starts immediately in
    /// place (legacy behaviour). Either way the request routes through
    /// <see cref="MerchantSpawner.PlayerInitiatesWithMerchant"/> so the spawner's one-at-a-time
    /// lock and fish check still apply.
    ///
    /// ONE CONVERSATION PER VISIT: the merchant engages the player exactly once per time they are in
    /// range. After a negotiation ends, it will NOT start another while the player stays in range —
    /// the player must LEAVE the range and come back to talk again. OnTriggerStay only keeps RE-TRYING
    /// the single engagement until it actually starts (e.g. when the spawner/mover was momentarily
    /// busy at the moment of entry); it never starts a second one.
    ///
    /// WALK-AWAY CANCEL: while THIS merchant is mid-negotiation, <see cref="Update"/> watches the
    /// player's distance and cancels the sale (the merchant walks back to its spot) once they move
    /// beyond <c>_conversationRange</c> for longer than the grace window. This is a plain distance
    /// check — no extra collider or child object is needed, and the range is independent of (and
    /// smaller than) the detection sphere, so the player doesn't have to leave detection range to end
    /// the conversation.
    /// </summary>
    [RequireComponent(typeof(MerchantNPC))]
    public class MerchantProximityTrigger : MonoBehaviour
    {
        [Tooltip("Tag identifying the player object entering the trigger.")]
        [SerializeField] private string _playerTag = "Player";

        [Tooltip("Spawner that owns the negotiation lock. Auto-resolved from the scene when empty.")]
        [SerializeField] private MerchantSpawner _spawner;

        [Tooltip("Legacy mode only (no MerchantApproachMover): rotate the merchant to face the " +
                 "player when they come into range.")]
        [SerializeField] private bool _facePlayerOnApproach = true;

        [Header("Conversation Range")]
        [Tooltip("While THIS merchant is negotiating, if the player moves further than this from the " +
                 "merchant the negotiation is cancelled and the merchant walks back to its spot. Must " +
                 "be larger than the mover's stop distance (~1.7 m) so simply standing to haggle stays " +
                 "in range. Smaller than the detection radius so walking off ends the sale.")]
        [SerializeField, Min(0.5f)] private float _conversationRange = 3f;

        [Tooltip("Seconds the player must stay BEYOND the conversation range before the negotiation is " +
                 "cancelled. A small value absorbs momentary boundary/VR jitter; 0 cancels instantly.")]
        [SerializeField, Min(0f)] private float _exitGraceSeconds = 0.75f;

        private MerchantNPC _merchant;
        private MerchantApproachMover _mover;

        // Player transform, captured when they enter the detection range, so the conversation-range
        // check can measure distance every frame while negotiating.
        private Transform _player;
        // Seconds the player has continuously been beyond the conversation range this negotiation.
        private float _outOfRangeTimer;

        // True once the merchant has engaged the player during the CURRENT time-in-range. Blocks any
        // further engagement until the player leaves and re-enters (reset in OnTriggerExit).
        private bool _engagedThisVisit;

        private void Awake()
        {
            _merchant = GetComponent<MerchantNPC>();
            _mover = GetComponent<MerchantApproachMover>();

            if (_spawner == null) _spawner = FindFirstObjectByType<MerchantSpawner>();
            if (_spawner == null)
                Debug.LogWarning($"MerchantProximityTrigger: no MerchantSpawner in scene for '{name}'; " +
                                 "approaches will do nothing.");
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(_playerTag)) return;
            _player = other.transform;   // remember the player for the conversation-range check
            // Fresh entry into range: allow exactly one engagement this visit.
            _engagedThisVisit = false;
            TryEngage(other);
        }

        // While THIS merchant is the one negotiating, cancel the sale (merchant walks back) if the
        // player wanders beyond the conversation range for longer than the grace window. This is a
        // distance check, not a trigger, so it works with no extra colliders/child objects — the
        // player never has to leave the (larger) detection sphere for the conversation to end.
        private void Update()
        {
            if (_spawner == null || _merchant == null) return;
            if (_spawner.ActiveMerchant != _merchant) { _outOfRangeTimer = 0f; return; }

            Transform player = ResolvePlayer();
            if (player == null) return;

            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;   // horizontal distance — VR head height must not affect it
            if (toPlayer.magnitude <= _conversationRange)
            {
                _outOfRangeTimer = 0f;
                return;
            }

            _outOfRangeTimer += Time.deltaTime;
            if (_outOfRangeTimer < _exitGraceSeconds) return;

            _outOfRangeTimer = 0f;
            Debug.Log($"MerchantProximityTrigger: player left '{name}' conversation range " +
                      $"({toPlayer.magnitude:0.#}m > {_conversationRange:0.#}m) — cancelling negotiation.");
            _spawner.PlayerLeftConversationArea(_merchant);
        }

        private Transform ResolvePlayer()
        {
            if (_player != null) return _player;
            GameObject go = GameObject.FindWithTag(_playerTag);
            if (go != null) _player = go.transform;
            return _player;
        }

        // Keep RE-TRYING the single engagement only until it actually starts — the spawner or mover may
        // have been busy at the instant of entry. Once engaged, this is a no-op until the player leaves.
        private void OnTriggerStay(Collider other)
        {
            if (_engagedThisVisit) return;
            TryEngage(other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(_playerTag)) return;
            // Player left the range: re-arm so a later re-entry can start a new conversation.
            _engagedThisVisit = false;
        }

        private void TryEngage(Collider other)
        {
            if (!other.CompareTag(_playerTag)) return;
            if (_spawner == null || _merchant == null) return;
            if (_engagedThisVisit) return;                 // already had this visit's one conversation
            // One NPC at a time: block while any merchant is negotiating OR already walking to/from the
            // player. Checking the spawner-wide state (not just this merchant's mover) is what stops two
            // merchants from approaching together. OnTriggerStay retries once the coast is clear.
            if (_spawner.IsAnyMerchantEngaging()) return;

            // Mark engaged BEFORE starting so nothing can slip a second conversation in this visit.
            _engagedThisVisit = true;

            if (_mover != null)
            {
                // The merchant walks to the player; the negotiation is initiated on arrival.
                _mover.ApproachAndInitiate(other.transform);
            }
            else
            {
                // Legacy in-place start.
                if (_facePlayerOnApproach) FaceToward(other.transform.position);
                _spawner.PlayerInitiatesWithMerchant(_merchant);
            }
        }

        // Yaw-only look-at so the merchant turns to the player without pitching the body.
        private void FaceToward(Vector3 worldPoint)
        {
            Vector3 flat = worldPoint - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
        }
    }
}
