using System.Collections;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    /// <summary>
    /// A "placement area" trigger around a merchant's <see cref="FishSellScale"/> that notices when the
    /// player releases a fish NEAR the scale but fails to land it on the scale (dropped on the ground
    /// beside it). Publishes <see cref="FishFumbledEvent"/> with <see cref="FishFumbleReason.MissedScale"/>
    /// so the negotiation can react with a light-hearted line while awaiting placement.
    ///
    /// Size this component's trigger a bit larger than the scale's own trigger. It only watches fish
    /// released inside this area; a fish thrown at the merchant is caught by
    /// <see cref="MerchantBodyBumpDetector"/> instead. Detection is a short settle check, so a fish that
    /// is picked straight back up or does come to rest on the scale is not reported.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class MissedScaleWatcher : MonoBehaviour
    {
        [Tooltip("The scale a released fish is supposed to land on. Auto-resolved from parent/children if empty.")]
        [SerializeField] private FishSellScale _scale;

        [Tooltip("Seconds to wait after a release before judging whether the fish landed on the scale.")]
        [SerializeField, Min(0f)] private float _settleSeconds = 1f;

        private IEventService _events;

        [Inject]
        private void Inject(IEventService events) => _events = events;

        // Fish overlapping the placement area, with their release listener registered.
        private readonly System.Collections.Generic.HashSet<Fish> _tracked =
            new System.Collections.Generic.HashSet<Fish>();

        private void Awake()
        {
            if (_scale == null)
                _scale = GetComponentInParent<FishSellScale>() ?? GetComponentInChildren<FishSellScale>();
            if (_scale == null)
                Debug.LogWarning($"MissedScaleWatcher on '{name}': no FishSellScale assigned or found — " +
                                 "missed-scale fumbles will not be detected.", this);
        }

        private void OnDestroy()
        {
            foreach (Fish fish in _tracked)
                if (fish != null && fish.TryGetComponent(out XRGrabInteractable grab))
                    grab.selectExited.RemoveListener(OnFishReleased);
            _tracked.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            Fish fish = Fish.FromCollider(other);
            if (fish == null || !_tracked.Add(fish)) return;
            if (fish.TryGetComponent(out XRGrabInteractable grab))
                grab.selectExited.AddListener(OnFishReleased);
        }

        private void OnTriggerExit(Collider other)
        {
            Fish fish = Fish.FromCollider(other);
            if (fish == null || !_tracked.Remove(fish)) return;
            if (fish.TryGetComponent(out XRGrabInteractable grab))
                grab.selectExited.RemoveListener(OnFishReleased);
        }

        // Released inside the placement area — check after a short settle whether it made it onto the scale.
        private void OnFishReleased(SelectExitEventArgs args)
        {
            if (args.interactableObject is XRGrabInteractable grab && grab.TryGetComponent(out Fish fish))
                StartCoroutine(CheckLanding(fish));
        }

        private IEnumerator CheckLanding(Fish fish)
        {
            yield return new WaitForSecondsRealtime(_settleSeconds);

            if (fish == null) yield break;                                                  // destroyed meanwhile
            if (fish.TryGetComponent(out XRGrabInteractable grab) && grab.isSelected) yield break; // picked back up
            if (_scale != null && _scale.Contains(fish)) yield break;                       // landed on the scale — fine

            _events?.Publish(new FishFumbledEvent(fish, FishFumbleReason.MissedScale));
        }
    }
}
