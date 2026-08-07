using UnityEngine;
using DumyatRuntime;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public class FishingQuestCompletion : MonoBehaviour
    {
        private IEventService _eventService;
        [SerializeField] private DialogueSequenceSO _fishingQuestCompletionDialogue;
        [SerializeField] private DialoguePriority _priority = DialoguePriority.Normal;
        [SerializeField] private bool _triggerOnce = true;


        [Inject]
        private void Inject(IEventService eventService)
        {
            _eventService = eventService;
        }

        private void OnEnable()
        {
            _eventService.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        private void OnDisable()
        {
            _eventService.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        private void OnQuestCompleted(QuestCompletedEvent evt)
        {
            if (evt.QuestID == "catch_4_fishes")
            {
                Debug.Log("Fishing quest completed");
                _eventService.Publish(new PlayDialogueEvent(_fishingQuestCompletionDialogue, _priority));
            }
        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}
