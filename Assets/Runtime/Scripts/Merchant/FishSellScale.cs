using System.Collections.Generic;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    /// <summary>The plan shape of a scale's acceptance region, inscribed in its trigger box.</summary>
    public enum ScaleFootprint
    {
        /// <summary>
        /// The ellipse inscribed in the trigger box. Correct for a ROUND basket: a box tight enough to
        /// hold the rim still leaves four corners of dead space beside it, and a fish set down there
        /// would otherwise have counted as sold.
        /// </summary>
        Ellipse,

        /// <summary>
        /// The trigger box itself. Correct for a RECTANGULAR crate, where an inscribed ellipse throws
        /// away the four corners — about a fifth of a floor the player can plainly see fish lying on.
        /// </summary>
        Rectangle
    }

    /// <summary>
    /// A merchant's physical fish scale. Its trigger collider identifies the <see cref="Fish"/> making
    /// contact and tracks which of them are actually RESTING IN it (released from the hand and settled
    /// inside the basket, not merely waved through while held or draped over the rim). This live set is
    /// the source of truth the negotiation reads to know the catch, and to re-verify the fish are still
    /// present before paying out.
    ///
    /// Placement model (mirrors <see cref="InventoryStashZone"/>): while a fish overlaps the trigger it
    /// is "tracked"; it counts as resting only while it is NOT being grabbed AND its centre of mass lies
    /// inside the trigger volume. Grabbing a resting fish drops it from the set; carrying it out of the
    /// volume removes it entirely. Identity is resolved via the <see cref="Fish"/> component (never
    /// <c>ItemPickup</c>), so a scale never contends with an inventory stash zone over the same object.
    ///
    /// The trigger is deliberately a tall box over the basket's interior rather than a hull of the basket
    /// mesh: overlap alone cannot see a fish stacked on top of another, so the volume extends well above
    /// the rim to take in the whole pile. Strictness comes from the centre-of-mass test in
    /// <see cref="IsInsideVolume"/>, not from the collider silhouette — a fish that is merely brushing
    /// the volume, or balanced half-outside the basket, does not count.
    ///
    /// The acceptance region is a shape INSCRIBED in the trigger box rather than the box itself, chosen
    /// by <see cref="_footprint"/> to match the container the merchant actually uses: an ellipse for a
    /// round basket, the box for a rectangular crate. The box always remains the (cheap) broadphase that
    /// decides what is tracked; the footprint decides what sells. Set it wrong and the scale silently
    /// refuses fish the player can see are sitting in the container — an ellipse in a crate discards the
    /// four corners, roughly a fifth of the floor.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class FishSellScale : MonoBehaviour, ISellScale
    {
        [Header("Placement")]
        [Tooltip("Plan shape of the acceptance region inside the trigger box. Ellipse for a round " +
                 "basket, Rectangle for a crate. Defaults to Ellipse so existing round-basket scales " +
                 "keep their behaviour; a crate MUST be set to Rectangle or its corners will not sell.")]
        [SerializeField] private ScaleFootprint _footprint = ScaleFootprint.Ellipse;

        [Tooltip("How often (seconds) to re-check that tracked fish are still inside the volume. Piles " +
                 "settle and shift after landing, so membership cannot be decided on trigger events alone.")]
        [SerializeField, Min(0.02f)] private float _recheckInterval = 0.2f;

        [Header("Feedback (optional)")]
        [Tooltip("Played through ISoundService (pooled, on the SFX bus) when a fish comes to rest on the scale.")]
        [SerializeField] private AudioClip _placeSound;

        [Range(0f, 1f)]
        [SerializeField] private float _placeVolume = 1f;

        // Injected so the scale can announce content changes on the shared bus. Optional — a scale in a
        // scene without a ContainerScope simply won't publish (it still tracks fish locally for polling).
        private IEventService _events;
        private IHapticService _haptics;
        private ISoundService _sound;

        [Inject]
        private void Inject(IEventService events, IHapticService haptics, ISoundService sound)
        {
            _events = events;
            _haptics = haptics;
            _sound = sound;
        }

        // The trigger box whose inscribed elliptical cylinder is the acceptance volume.
        private BoxCollider _volume;

        // Fish whose colliders currently overlap the trigger, mapped to HOW MANY of their colliders are
        // inside. A fish built from several colliders raises multiple enter/exit pairs; without the count
        // the first exit would untrack a fish that is still sitting in the basket.
        private readonly Dictionary<Fish, int> _tracked = new Dictionary<Fish, int>();

        // The subset of tracked fish that are released and inside — the sellable set.
        private readonly List<Fish> _resting = new List<Fish>();

        // Scratch buffer so the periodic re-check can mutate _tracked while iterating it.
        private readonly List<Fish> _evaluationBuffer = new List<Fish>();

        private float _recheckTimer;

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
            ResolveVolume();
        }

        private void OnDestroy()
        {
            // Drop every grab listener we registered so we never call back into a destroyed scale.
            foreach (Fish fish in _tracked.Keys)
                Unsubscribe(fish);
            _tracked.Clear();
            _resting.Clear();
        }

        // Piles settle, roll and shift long after the trigger events stop firing, so membership is
        // re-derived on a slow tick rather than trusted from enter/exit alone.
        private void Update()
        {
            if (_tracked.Count == 0) return;

            _recheckTimer += Time.deltaTime;
            if (_recheckTimer < _recheckInterval) return;
            _recheckTimer = 0f;

            _evaluationBuffer.Clear();
            foreach (Fish fish in _tracked.Keys) _evaluationBuffer.Add(fish);

            foreach (Fish fish in _evaluationBuffer)
            {
                if (fish == null) { Untrack(fish); continue; }
                Evaluate(fish);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            Fish fish = Fish.FromCollider(other);
            if (fish == null) return;

            if (_tracked.TryGetValue(fish, out int overlaps))
            {
                _tracked[fish] = overlaps + 1;      // another collider of an already-tracked fish
            }
            else
            {
                _tracked[fish] = 1;
                if (fish.TryGetComponent(out XRGrabInteractable grab))
                {
                    grab.selectEntered.AddListener(OnTrackedFishGrabbed);
                    grab.selectExited.AddListener(OnTrackedFishReleased);
                }
            }

            Evaluate(fish);
        }

        private void OnTriggerExit(Collider other)
        {
            Fish fish = Fish.FromCollider(other);
            if (fish == null || !_tracked.TryGetValue(fish, out int overlaps)) return;

            if (overlaps > 1)
            {
                _tracked[fish] = overlaps - 1;      // still has other colliders inside
                Evaluate(fish);
                return;
            }

            Untrack(fish);
        }

        // Grabbed while over the scale — no longer resting (the player is taking it back).
        private void OnTrackedFishGrabbed(SelectEnterEventArgs args)
        {
            if (FishOf(args) is Fish fish) Evaluate(fish);
        }

        // Released while still over the scale (an exit would have removed tracking first) — may now rest.
        private void OnTrackedFishReleased(SelectExitEventArgs args)
        {
            if (FishOf(args) is Fish fish) Evaluate(fish);
        }

        /// <summary>
        /// Single decision point for membership: a tracked fish counts only while it is released AND its
        /// centre of mass is inside the acceptance volume.
        /// </summary>
        private void Evaluate(Fish fish)
        {
            if (fish == null) return;

            bool held = fish.TryGetComponent(out XRGrabInteractable grab) && grab.isSelected;
            SetResting(fish, _tracked.ContainsKey(fish) && !held && IsInsideVolume(fish));
        }

        /// <summary>
        /// Containment test: is the fish inside the elliptical cylinder inscribed in the trigger box?
        /// Uses the fish's centre of mass (its body, not the tip of a fin) so a fish merely brushing the
        /// volume, hanging over the rim, or sitting in a corner beside the round basket does not count.
        /// </summary>
        private bool IsInsideVolume(Fish fish)
        {
            if (_volume == null) return true;   // no volume resolved — fall back to plain overlap

            Rigidbody body = fish.GetComponent<Rigidbody>();
            return IsPointInside(body != null ? body.worldCenterOfMass : fish.transform.position);
        }

        /// <summary>
        /// True if <paramref name="worldPoint"/> lies within the scale's acceptance volume — the prism
        /// inscribed in the trigger box whose plan shape is <see cref="_footprint"/>. Public so
        /// placement aids and diagnostics can ask the same question the sale does, rather than
        /// re-deriving the shape.
        /// </summary>
        public bool IsPointInside(Vector3 worldPoint)
        {
            if (_volume == null) ResolveVolume();
            if (_volume == null) return false;

            // Work in the box's own space, so the test follows the basket's rotation and scale for free.
            Vector3 local = transform.InverseTransformPoint(worldPoint) - _volume.center;
            Vector3 half = _volume.size * 0.5f;

            if (half.x <= 0f || half.y <= 0f || half.z <= 0f) return false;
            if (Mathf.Abs(local.y) > half.y) return false;               // below the floor / above the pile

            // Normalising by the half-extents turns the box into a unit square and the inscribed
            // ellipse into a unit circle, so both footprints are one comparison against 1.
            float x = local.x / half.x;
            float z = local.z / half.z;
            return _footprint == ScaleFootprint.Rectangle
                ? Mathf.Abs(x) <= 1f && Mathf.Abs(z) <= 1f
                : x * x + z * z <= 1f;
        }

        /// <summary>
        /// Picks the acceptance volume out of the box colliders on this GameObject, preferring a trigger.
        /// Deliberately not a bare <c>TryGetComponent</c>: <c>[RequireComponent]</c> hands us a default
        /// 1x1x1 box whenever the requirement looks unsatisfied (e.g. after the attribute changes), and
        /// silently adopting that would both shrink the volume to a meaningless cube and — since the
        /// added box is NOT a trigger — drop a solid metre-wide collider onto the merchant's basket.
        /// </summary>
        private void ResolveVolume()
        {
            _volume = null;
            var boxes = GetComponents<BoxCollider>();

            foreach (BoxCollider box in boxes)
            {
                if (!box.isTrigger) continue;
                if (_volume == null) { _volume = box; continue; }
                Debug.LogWarning($"FishSellScale on '{name}': more than one trigger BoxCollider — using " +
                                 $"the first (size {_volume.size}). Remove the spares.", this);
                break;
            }

            if (_volume == null)
                Debug.LogWarning($"FishSellScale on '{name}': no trigger BoxCollider found — fish placement " +
                                 "will not be detected. Enable 'Is Trigger' on the acceptance volume.", this);

            foreach (BoxCollider box in boxes)
                if (!box.isTrigger)
                    Debug.LogWarning($"FishSellScale on '{name}': a NON-trigger BoxCollider is present and " +
                                     "will physically block fish from entering the basket. Remove it.", this);
        }

        private void Untrack(Fish fish)
        {
            _tracked.Remove(fish);
            Unsubscribe(fish);
            SetResting(fish, false);
        }

        private void Unsubscribe(Fish fish)
        {
            if (fish != null && fish.TryGetComponent(out XRGrabInteractable grab))
            {
                grab.selectEntered.RemoveListener(OnTrackedFishGrabbed);
                grab.selectExited.RemoveListener(OnTrackedFishReleased);
            }
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
            // Pooled + routed to the SFX mixer bus, so the settings volume slider applies.
            if (_placeSound != null) _sound?.PlaySoundFxAtPosition(_placeSound, transform, _placeVolume);
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

#if UNITY_EDITOR
        // Draws the ACCEPTANCE volume, not merely the box, so what is drawn is what actually counts.
        // Size the box handles until this shape matches the container interior and clears the tallest pile.
        private void OnDrawGizmosSelected()
        {
            BoxCollider box = null;
            foreach (BoxCollider candidate in GetComponents<BoxCollider>())
                if (candidate.isTrigger) { box = candidate; break; }
            if (box == null) return;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 1f);

            // A rectangular footprint accepts the whole box, so the box IS the acceptance volume.
            if (_footprint == ScaleFootprint.Rectangle)
            {
                Gizmos.DrawWireCube(box.center, box.size);
                return;
            }

            Vector3 half = box.size * 0.5f;
            const int segments = 32;

            for (int i = 0; i < segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                float b = ((i + 1) / (float)segments) * Mathf.PI * 2f;
                var from = new Vector3(Mathf.Cos(a) * half.x, 0f, Mathf.Sin(a) * half.z);
                var to = new Vector3(Mathf.Cos(b) * half.x, 0f, Mathf.Sin(b) * half.z);
                Vector3 up = Vector3.up * half.y;

                Gizmos.DrawLine(box.center + from - up, box.center + to - up);   // floor ring
                Gizmos.DrawLine(box.center + from + up, box.center + to + up);   // top ring
                if (i % 8 == 0) Gizmos.DrawLine(box.center + from - up, box.center + from + up);
            }
        }
#endif
    }
}
