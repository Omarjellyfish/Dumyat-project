using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Joins a fish to the hook and announces the catch.
    ///
    /// Attachment used to be reachable only through <see cref="OnTriggerEnter"/>, which meant the catch
    /// depended on a fish moving 347 mm per frame overlapping a sphere of 12.5 mm world radius — a
    /// coin flip whose odds were a direct function of frame time, and one that a locked 72 Hz headset
    /// lost far more often than an uncapped Editor. <see cref="TryAttach"/> is now public, so the bite
    /// schedule can hook a fish that has demonstrably arrived instead of waiting for physics to notice.
    /// The trigger path is unchanged and still works — it is just no longer the only way in.
    /// </summary>
    public class HookingFish : MonoBehaviour
    {
        private bool _isFishHooked;
        private FixedJoint _currentJoint;
        private FishBehaviour _hookedFish;
        private IHapticService _haptics;

        [Header("Audio Feedback")]
        [Tooltip("The father's line for the moment a fish takes the hook. Auto-resolved from this " +
                 "GameObject or its parents if empty. Owns its own 'play once' and per-scene rules.")]
        [SerializeField] private OneShotDialogueCue _hookedCue;

        [Inject]
        private void Inject(IHapticService haptics) => _haptics = haptics;

        /// <summary>True while a fish is jointed to this hook.</summary>
        public bool HasFish => _isFishHooked;

        private void Awake()
        {
            if (_hookedCue == null) _hookedCue = GetComponentInParent<OneShotDialogueCue>();
        }

        private void OnTriggerEnter(Collider collision)
        {
            if (_isFishHooked) return;
            if (!collision.CompareTag("Fish")) return;

            TryAttach(Fish.FromCollider(collision));
        }

        /// <summary>
        /// Joint this fish to the hook. Returns false when the hook is already occupied or the fish
        /// cannot be jointed. Safe to call more than once for the same fish.
        /// </summary>
        public bool TryAttach(Fish fish)
        {
            if (_isFishHooked) return false;
            if (fish == null) return false;

            Rigidbody fishBody = fish.GetComponent<Rigidbody>();
            Rigidbody hookBody = GetComponentInParent<Rigidbody>();

            if (fishBody == null || hookBody == null) return false;

            // A kinematic body cannot be driven by a joint. The fish turns dynamic on arrival, but a fish
            // that reached the hook by swimming into the trigger may not have gone through that path.
            if (fishBody.isKinematic)
            {
                fishBody.isKinematic = false;
                fishBody.useGravity = true;
            }

            fishBody.linearVelocity = Vector3.zero;
            fishBody.angularVelocity = Vector3.zero;

            FixedJoint joint = fish.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = hookBody;
            joint.autoConfigureConnectedAnchor = false;
            joint.breakForce = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;

            // Anchor at the mouth when the prefab authors one, so the fish hangs off the hook by its lip
            // rather than by its centre of mass.
            Transform mouth = fish.MouthPoint != null ? fish.MouthPoint : fish.transform;
            joint.anchor = fish.transform.InverseTransformPoint(mouth.position);
            joint.connectedAnchor = hookBody.transform.InverseTransformPoint(mouth.position);

            fish.StopAnimation();

            _currentJoint = joint;
            _isFishHooked = true;
            _hookedFish = fish.GetComponent<FishBehaviour>();

            _hookedCue?.TryPlay();
            _haptics?.TriggerVibration(1f, 2f, 0.5f, HapticHand.Right);

            fish.OnCaught();
            return true;
        }

        private void Update()
        {
            // If we think a fish is hooked but its FixedJoint is gone (the player pulled it off, or the
            // fish was destroyed after being sold), re-arm so the next fish can be caught. This does not
            // depend on anything external calling ResetHook at the right moment.
            if (_isFishHooked && _currentJoint == null)
            {
                ReleaseFish();
            }
        }

        /// <summary>
        /// Let go of the current fish and re-arm the hook.
        ///
        /// A fish that was genuinely caught stays caught — it belongs to the player now, and pushing it
        /// back into the shoal would have it swim out of their hand. Only a fish that never completed the
        /// catch is returned, which is the case that used to strand it: <c>isHooked</c> latched true with
        /// no joint to show for it, and the fish neither patrolled nor could ever be hooked again.
        /// </summary>
        public void ReleaseFish()
        {
            if (_hookedFish != null)
            {
                Fish fish = _hookedFish.GetComponent<Fish>();
                if (fish == null || !fish.IsCaught) _hookedFish.ReleaseFromHook();
                _hookedFish = null;
            }

            _isFishHooked = false;
            _currentJoint = null;
        }

        /// <summary>
        /// Manual re-arm, e.g. when the rod is re-cast and the hook should accept a new fish immediately.
        /// Refuses while a joint is actually live: clearing our state out from under a real catch would
        /// leave the fish physically attached to a hook that believes it is empty, and the next fish
        /// would be jointed on top of it.
        /// </summary>
        public void ResetHook()
        {
            if (_currentJoint != null) return;

            ReleaseFish();
        }
    }
}
