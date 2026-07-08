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

        private MerchantNPC _merchant;
        private MerchantApproachMover _mover;

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
            // Fresh entry into range: allow exactly one engagement this visit.
            _engagedThisVisit = false;
            TryEngage(other);
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
            if (_spawner.IsNegotiationActive) return;      // busy now — OnTriggerStay retries later
            if (_mover != null && _mover.IsBusy) return;

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
