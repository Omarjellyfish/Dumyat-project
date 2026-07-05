using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace DumyatRuntime
{
    public static class XRInteractionUtils
    {
        public static HapticHand GetGrabbingHand(XRGrabInteractable grabInteractable)
        {
            if (grabInteractable == null || grabInteractable.interactorsSelecting.Count == 0)
                return HapticHand.Right;

            IXRSelectInteractor interactor = grabInteractable.interactorsSelecting[0];
            string interactorName = (interactor as MonoBehaviour)?.gameObject.name.ToLower() ?? "";
            return interactorName.Contains("left") ? HapticHand.Left : HapticHand.Right;
        }
    }
}
