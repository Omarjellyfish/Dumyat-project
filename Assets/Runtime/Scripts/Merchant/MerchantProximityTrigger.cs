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
    /// lock, fish check and cooldown still apply. Attempts re-arm on a local cooldown via
    /// OnTriggerStay, so a player who stays in range can be approached again after a finished
    /// negotiation without having to leave and come back.
    /// </summary>
    [RequireComponent(typeof(MerchantNPC))]
    public class MerchantProximityTrigger : MonoBehaviour
    {
        [Tooltip("Tag identifying the player object entering the trigger.")]
        [SerializeField] private string _playerTag = "Player";

        [Tooltip("Spawner that owns the negotiation lock. Auto-resolved from the scene when empty.")]
        [SerializeField] private MerchantSpawner _spawner;

        [Tooltip("Seconds between approach attempts while the player stays in range. Keep it at or " +
                 "above the spawner's cooldown so retries aren't pointlessly rejected.")]
        [SerializeField, Min(1f)] private float _reApproachCooldown = 8f;

        [Tooltip("Legacy mode only (no MerchantApproachMover): rotate the merchant to face the " +
                 "player when they come into range.")]
        [SerializeField] private bool _facePlayerOnApproach = true;

        private MerchantNPC _merchant;
        private MerchantApproachMover _mover;
        private float _nextAttemptTime;

        private void Awake()
        {
            _merchant = GetComponent<MerchantNPC>();
            _mover = GetComponent<MerchantApproachMover>();

            if (_spawner == null) _spawner = FindFirstObjectByType<MerchantSpawner>();
            if (_spawner == null)
                Debug.LogWarning($"MerchantProximityTrigger: no MerchantSpawner in scene for '{name}'; " +
                                 "approaches will do nothing.");
        }

        private void OnTriggerEnter(Collider other) => TryEngage(other);

        // Re-attempt while the player lingers in range (they may never re-enter after a finished
        // negotiation). The local cooldown keeps this from spamming the spawner every physics tick.
        private void OnTriggerStay(Collider other) => TryEngage(other);

        private void TryEngage(Collider other)
        {
            if (!other.CompareTag(_playerTag)) return;
            if (_spawner == null || _merchant == null) return;
            if (Time.time < _nextAttemptTime) return;
            if (_spawner.IsNegotiationActive) return;
            if (_mover != null && _mover.IsBusy) return;

            _nextAttemptTime = Time.time + _reApproachCooldown;

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
