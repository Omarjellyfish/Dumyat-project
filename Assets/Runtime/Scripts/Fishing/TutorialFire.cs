using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public class TutorialFire : MonoBehaviour
    {
        private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable _interactable;
        [SerializeField] private DialogueSequenceSO _dialogueSequence;
        [SerializeField] private DialoguePriority _priority = DialoguePriority.Normal;
        private IEventService _eventService;
        private bool hasTriggered = false;
        [SerializeField] private bool _triggerOnce = true;

        [Inject]
        private void Inject(IEventService eventService)
        {
            _eventService = eventService;
        }

        private void Awake()
        {
            _interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        }

        private void OnEnable()
        {
            _interactable.selectEntered.AddListener(OnSelectEntered);
        }

        private void OnDisable()
        {
            _interactable.selectEntered.RemoveListener(OnSelectEntered);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (_triggerOnce && hasTriggered) return;
            hasTriggered = true;
            Debug.Log("Fire selected");
            _eventService.Publish(new PlayDialogueEvent(_dialogueSequence, _priority));

        }
    }
}
