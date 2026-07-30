using Reflex.Attributes;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    /// <summary>
    /// A trigger volume on the merchant's body that notices when the player throws or drops a fish onto
    /// the merchant instead of the scale. Publishes <see cref="FishFumbledEvent"/> with
    /// <see cref="FishFumbleReason.HitMerchant"/> so the negotiation can react with a light-hearted line
    /// (it only does so while awaiting fish placement).
    ///
    /// Only a fish that is NOT currently held counts — a thrown/dropped fish is released, whereas a fish
    /// carried near the merchant is still selected and ignored. A short cooldown stops a fish that lands
    /// and rolls on the merchant from spamming the event.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class MerchantBodyBumpDetector : MonoBehaviour
    {
        [Tooltip("Minimum seconds between reported bumps, so a fish rolling on the merchant fires once.")]
        [SerializeField, Min(0f)] private float _rebumpCooldownSeconds = 1.5f;

        private IEventService _events;

        [Inject]
        private void Inject(IEventService events) => _events = events;

        private float _nextAllowedTime;

        private void OnTriggerEnter(Collider other)
        {
            Fish fish = Fish.FromCollider(other);
            if (fish == null) return;

            // A held fish means the player is just moving it past the merchant — not a fumble.
            if (fish.TryGetComponent(out XRGrabInteractable grab) && grab.isSelected) return;

            if (Time.unscaledTime < _nextAllowedTime) return;
            _nextAllowedTime = Time.unscaledTime + _rebumpCooldownSeconds;

            _events?.Publish(new FishFumbledEvent(fish, FishFumbleReason.HitMerchant));
        }
    }
}
