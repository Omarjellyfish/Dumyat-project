using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    /// <summary>
    /// Takes the fish off the hook when the player gets a hand on it, by destroying the
    /// <see cref="FixedJoint"/> that <see cref="HookingFish"/> added.
    ///
    /// One job. It used to also play the father's "respectable fish" line, with its own copy of a
    /// play-once check against a save key — while the clip field was null on every fish prefab, so it
    /// played silence and burned the key regardless. Worse, it read its injected services without a
    /// null guard, so a fish that was never injected threw here and never got dehooked at all. The
    /// line is a <see cref="DialogueCueOnGrab"/> on the prefab now; this is what is left.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class DehookingFish : MonoBehaviour
    {
        private XRGrabInteractable _grabInteractable;

        private void Awake()
        {
            _grabInteractable = GetComponent<XRGrabInteractable>();
        }

        private void OnEnable()
        {
            _grabInteractable.selectEntered.AddListener(OnGrabbed);
        }

        private void OnDisable()
        {
            _grabInteractable.selectEntered.RemoveListener(OnGrabbed);
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            // Destroy any joints attached directly to this fish
            FixedJoint[] localJoints = GetComponentsInChildren<FixedJoint>();
            foreach (FixedJoint joint in localJoints)
            {
                Destroy(joint);
            }

            // The joint might be attached to the hook instead of the fish.
            // Since we avoid FindObjectOfType at runtime, we check nearby rigidbodies.
            Rigidbody myRb = GetComponent<Rigidbody>();
            if (myRb != null)
            {
                Collider[] nearbyColliders = Physics.OverlapSphere(
                    transform.position, 3f, ~0, QueryTriggerInteraction.Ignore);
                foreach (Collider col in nearbyColliders)
                {
                    if (col.attachedRigidbody != null && col.attachedRigidbody != myRb)
                    {
                        FixedJoint[] externalJoints = col.attachedRigidbody.GetComponents<FixedJoint>();
                        foreach (FixedJoint joint in externalJoints)
                        {
                            if (joint.connectedBody == myRb)
                            {
                                Destroy(joint);
                            }
                        }
                    }
                }
            }
        }
    }
}
