using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    public class DehookingFish : MonoBehaviour
    {
        private XRGrabInteractable grabInteractable;
        private void Awake()
        {
            grabInteractable = GetComponent<XRGrabInteractable>();
        }

        private void OnEnable()
        {
            grabInteractable.selectEntered.AddListener(OnGrabbed);
        }

        private void OnDisable()
        {
            grabInteractable.selectEntered.RemoveListener(OnGrabbed);
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            FixedJoint joint = GetComponent<FixedJoint>();

            if (joint != null)
            {
                Destroy(joint);
            }
        }
    }
}
