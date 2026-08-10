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
            FixedJoint joint = GetComponent<FixedJoint>();
            if (joint != null) Destroy(joint);
        }
    }
}
