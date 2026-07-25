using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public class Fish : MonoBehaviour
    {
        public FishyData data;

        public float currentWeight;
        public float currentValue;
        private bool isCaught;
        [SerializeField] private Transform mouthPoint; // drag mouth empty/bone here in Inspector

        public Transform MouthPoint => mouthPoint;
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

        private void Start()
        {
            Animator fishAnimator = GetComponentInChildren<Animator>();
            fishAnimator.SetBool("isHooked", false);
            _interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            _interactable.selectEntered.AddListener(OnSelectEntered);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (_triggerOnce && hasTriggered) return;
            hasTriggered = true;
            Debug.Log("Fish selected");
            _eventService.Publish(new PlayDialogueEvent(_dialogueSequence, _priority));

        }
        public void StopAnimation()
        {
            Animator fishAnimator = GetComponentInChildren<Animator>();
            fishAnimator.SetBool("isHooked", true);
        }
        public void Initialize(FishyData fishData)
        {
            data = fishData;

            currentWeight = Random.Range(
                data.minWeight,
                data.maxWeight);

            currentValue = currentWeight * data.pricePerKg;
        }
        public void OnCaught()
        {
            if (isCaught)
                return;

            isCaught = true;

            if (data == null)
            {
                Debug.LogError("FishyData is NULL!");
                return;
            }

            currentWeight = Random.Range(data.minWeight, data.maxWeight);
            currentValue = currentWeight * data.pricePerKg;

            Debug.Log($"Caught {data.fishName}");
            Debug.Log($"Weight: {currentWeight}");
            Debug.Log($"Value: {currentValue}");

            _eventService.Publish(new FishCaughtEvent(data.fishName, currentWeight, currentValue));
        }
    }
}
