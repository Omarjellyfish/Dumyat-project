using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public class HookingFish : MonoBehaviour
    {
        private bool isFishHooked = false;
        private FixedJoint currentJoint;
        private IHapticService _haptics;

        [Header("Audio Feedback")]
        [Tooltip("Voice line for the moment a fish takes the hook. Auto-resolved from this GameObject " +
                 "or its parents if empty. Owns its own 'play once' and per-scene rules.")]
        [SerializeField] private OneShotVoiceCue _hookVoice;

        [Inject]
        private void Inject(IHapticService haptics) => _haptics = haptics;

        private void Awake()
        {
            if (_hookVoice == null) _hookVoice = GetComponentInParent<OneShotVoiceCue>();
        }

        private void OnTriggerEnter(Collider collision)
        {
            if (isFishHooked) return;
            if (!collision.CompareTag("Fish")) return;

            Fish fish = collision.GetComponent<Fish>();
            Rigidbody fishBody = collision.attachedRigidbody;
            Rigidbody hookColliderRigidbody = GetComponentInParent<Rigidbody>();

            if (fish == null || fishBody == null || hookColliderRigidbody == null)
                return;

            // Transform mouth = fish.MouthPoint != null ? fish.MouthPoint : fishBody.transform;

            fishBody.linearVelocity = Vector3.zero;
            fishBody.angularVelocity = Vector3.zero;

            FixedJoint joint = fish.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = hookColliderRigidbody;
            joint.autoConfigureConnectedAnchor = false;
            fish.StopAnimation();

            // anchor: local to the fish (this object the joint sits on)
            // joint.anchor = fishBody.transform.InverseTransformPoint(mouth.position);

            // connectedAnchor: local to the hook, i.e. where on the hook the mouth should sit
            // joint.connectedAnchor = hookColliderRigidbody.transform.InverseTransformPoint(mouth.position);

            joint.breakForce = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;

            currentJoint = joint;
            isFishHooked = true;

            _hookVoice?.TryPlay();

            _haptics?.TriggerVibration(1f, 2f, 0.5f, HapticHand.Right);

            fish.OnCaught();
            Debug.Log("Fish hooked");
        }

        private void Update()
        {
            // If we think a fish is hooked but its FixedJoint is gone
            // (fish destroyed/despawned after being caught, or the joint
            // was otherwise removed), automatically re-arm the hook so the
            // next fish that swims in can actually get a FixedJoint.
            // This does not depend on any external script calling ResetHook()
            // at the right time.
            if (isFishHooked && currentJoint == null)
            {
                isFishHooked = false;
            }
        }

        // Optional manual reset, e.g. call this when the rod is reset/recast
        // if you want the hook to accept a new fish immediately rather than
        // waiting for the previous fish/joint to actually be destroyed.
        public void ResetHook()
        {
            isFishHooked = false;
            currentJoint = null;
        }
    }
}