using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class InteractDialogueTrigger : DialogueTrigger
    {
        private XRSimpleInteractable _interactable;

        private void Awake()
        {
            _interactable = GetComponent<XRSimpleInteractable>();
        }

        private void OnEnable()
        {
            _interactable.selectEntered.AddListener(HandleInteraction);
        }

        private void OnDisable()
        {
            _interactable.selectEntered.RemoveListener(HandleInteraction);
        }

        private void HandleInteraction(SelectEnterEventArgs args)
        {
            TryTriggerDialogue();
        }
    }
}