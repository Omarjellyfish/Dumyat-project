using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    /// <summary>
    /// Frees a caught fish from the hook when the player takes hold of it, and lets the accompanying
    /// voice line fire.
    ///
    /// The line itself is NOT owned here — a <see cref="OneShotVoiceCue"/> on the same GameObject
    /// decides whether it has already been heard and whether this scene should hear it at all. That
    /// split matters because every fish carries its own copy of this component: the "play once"
    /// decision has to be shared between them, which is the cue's job, not this one's.
    /// </summary>
    public class DehookingFish : MonoBehaviour
    {
        [Tooltip("Voice line for taking the fish off the hook. Auto-resolved from this GameObject if " +
                 "empty; leave the cue off entirely for a silent fish.")]
        [SerializeField] private OneShotVoiceCue _catchVoice;

        private XRGrabInteractable _grabInteractable;

        private void Awake()
        {
            _grabInteractable = GetComponent<XRGrabInteractable>();
            if (_catchVoice == null) _catchVoice = GetComponent<OneShotVoiceCue>();
        }

        private void OnEnable()
        {
            if (_grabInteractable != null) _grabInteractable.selectEntered.AddListener(OnGrabbed);
        }

        private void OnDisable()
        {
            if (_grabInteractable != null) _grabInteractable.selectEntered.RemoveListener(OnGrabbed);
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            _catchVoice?.TryPlay();

            // The joint is what pins the fish to the hook. Dropping it is what "dehooking" means, and
            // it has to happen whether or not there was a line to play.
            FixedJoint joint = GetComponent<FixedJoint>();
            if (joint != null) Destroy(joint);
        }
    }
}
