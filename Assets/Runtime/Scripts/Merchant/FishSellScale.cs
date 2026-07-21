using System.Collections.Generic;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    /// <summary>
    /// A merchant's physical fish scale. Its trigger collider identifies the <see cref="Fish"/> making
    /// contact and tracks which of them are actually RESTING on it (released from the hand, not merely
    /// waved through while held). This live set is the source of truth the negotiation reads to know
    /// the catch, and to re-verify the fish are still present before paying out.
    ///
    /// Placement model (mirrors <see cref="InventoryStashZone"/>): while a fish overlaps the trigger it
    /// is "tracked"; it counts as resting only while it is NOT being grabbed. Grabbing a resting fish
    /// drops it from the set; carrying it out of the volume removes it entirely. Identity is resolved
    /// via the <see cref="Fish"/> component (never <c>ItemPickup</c>), so a scale never contends with an
    /// inventory stash zone over the same object.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class FishSellScale : MonoBehaviour, ISellScale
    {
        [Header("Feedback (optional)")]
        [Tooltip("Played when a fish comes to rest on the scale.")]
        [SerializeField] private AudioClip _placeSound;
        [SerializeField] private AudioSource _audioSource;

        // Injected so the scale can announce content changes on the shared bus. Optional — a scale in a
        // scene without a ContainerScope simply won't publish (it still tracks fish locally for polling).
        private IEventService _events;
        private IHapticService _haptics;

        [Inject]
        private void Inject(IEventService events, IHapticService haptics)
        {
            _events = events;
            _haptics = haptics;
        }

        // Fish whose colliders currently overlap the trigger (grab listeners registered for these).
        private readonly HashSet<Fish> _tracked = new HashSet<Fish>();

        // The subset of tracked fish that are released and resting — the sellable set.
        private readonly List<Fish> _resting = new List<Fish>();

        public IReadOnlyList<Fish> FishOnScale
        {
            get { Prune(); return _resting; }
        }

        public bool HasFish
        {
            get { Prune(); return _resting.Count > 0; }
        }

        /// <summary>True if <paramref name="fish"/> is currently resting on the scale.</summary>
        public bool Contains(Fish fish) => fish != null && _resting.Contains(fish);

        public float TotalWeightKg
        {
            get
            {
                Prune();
                float total = 0f;
                foreach (Fish fish in _resting)
                    if (fish != null) total += fish.Weight;
                return total;
            }
        }

        public IReadOnlyList<FishData> FishDataOnScale
        {
            get
            {
                Prune();
                var data = new List<FishData>(_resting.Count);
                foreach (Fish fish in _resting)
                {
                    FishData d = fish != null ? fish.ToFishData() : null;
                    if (d != null) data.Add(d);
                }
                return data;
            }
        }

        private void Awake()
        {
            // Fall back to an AudioSource on this GameObject so placement audio works without manual wiring.
            if (_audioSource == null) TryGetComponent(out _audioSource);

            if (TryGetComponent(out Collider col) && !col.isTrigger)
                Debug.LogWarning($"FishSellScale on '{name}': its Collider is not set as a trigger — " +
                                 "fish placement will not be detected. Enable 'Is Trigger'.", this);
        }

        private void OnDestroy()
        {
            // Drop every grab listener we registered so we never call back into a destroyed scale.
            foreach (Fish fish in _tracked)
            {
                if (fish != null && fish.TryGetComponent(out XRGrabInteractable grab))
                {
                    grab.selectEntered.RemoveListener(OnTrackedFishGrabbed);
                    grab.selectExited.RemoveListener(OnTrackedFishReleased);
                }
            }
            _tracked.Clear();
            _resting.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            Fish fish = Fish.FromCollider(other);
            if (fish == null || !_tracked.Add(fish)) return;   // null, or already tracked (extra collider)

            if (fish.TryGetComponent(out XRGrabInteractable grab))
            {
                grab.selectEntered.AddListener(OnTrackedFishGrabbed);
                grab.selectExited.AddListener(OnTrackedFishReleased);
                // Dropped straight in (not held) → resting immediately. Held → wait for the release.
                if (!grab.isSelected) SetResting(fish, true);
            }
            else
            {
                SetResting(fish, true);   // no grab component — treat as placed
            }
        }

        private void OnTriggerExit(Collider other)
        {
            Fish fish = Fish.FromCollider(other);
            if (fish == null || !_tracked.Remove(fish)) return;

            if (fish.TryGetComponent(out XRGrabInteractable grab))
            {
                grab.selectEntered.RemoveListener(OnTrackedFishGrabbed);
                grab.selectExited.RemoveListener(OnTrackedFishReleased);
            }
            SetResting(fish, false);
        }

        // Grabbed while over the scale — no longer resting (the player is taking it back).
        private void OnTrackedFishGrabbed(SelectEnterEventArgs args)
        {
            if (FishOf(args) is Fish fish) SetResting(fish, false);
        }

        // Released while still over the scale (an exit would have removed tracking first) — now resting.
        private void OnTrackedFishReleased(SelectExitEventArgs args)
        {
            if (FishOf(args) is Fish fish && _tracked.Contains(fish)) SetResting(fish, true);
        }

        private void SetResting(Fish fish, bool resting)
        {
            if (resting)
            {
                if (fish == null || _resting.Contains(fish)) return;
                _resting.Add(fish);
                PlayPlacedFeedback();
            }
            else if (!_resting.Remove(fish))
            {
                return;
            }

            _events?.Publish(new ScaleContentsChangedEvent(this));
        }

        private void PlayPlacedFeedback()
        {
            if (_audioSource != null && _placeSound != null) _audioSource.PlayOneShot(_placeSound);
            _haptics?.TriggerVibration(0.4f, 0.1f, 0.6f, HapticHand.Both);
        }

        // Drops fish that were destroyed (e.g. sold) without an OnTriggerExit — Unity does not
        // guarantee that callback when the other collider's GameObject is destroyed.
        private void Prune()
        {
            for (int i = _resting.Count - 1; i >= 0; i--)
                if (_resting[i] == null) _resting.RemoveAt(i);
        }

        private static Fish FishOf(BaseInteractionEventArgs args)
            => args.interactableObject is XRGrabInteractable grab && grab.TryGetComponent(out Fish fish)
                ? fish
                : null;
    }
}
