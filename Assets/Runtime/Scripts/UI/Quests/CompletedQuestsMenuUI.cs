using UnityEngine;
using UnityEngine.UIElements;
using Reflex.Attributes;

namespace DumyatRuntime
{
    [RequireComponent(typeof(UIDocument))]
    public class CompletedQuestsMenuUI : MonoBehaviour
    {
        [SerializeField] private VisualTreeAsset _completedQuestTemplate;

        private IQuestService _questService;
        private IEventService _eventService;
        
        private VisualElement _listContainer;

        [Inject]
        public void Construct(IQuestService questService, IEventService eventService)
        {
            _questService = questService;
            _eventService = eventService;
        }

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null) return;

            _listContainer = root.Q<VisualElement>("CompletedListContainer");

            if (_questService != null && _eventService != null)
            {
                RefreshList();
                _eventService.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
            }
        }

        private void OnDisable()
        {
            if (_eventService != null)
            {
                _eventService.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
            }
        }

        private void RefreshList()
        {
            if (_listContainer == null || _completedQuestTemplate == null) return;
            
            _listContainer.Clear();

            var completedQuests = _questService.GetCompletedQuests();
            foreach (var quest in completedQuests)
            {
                var itemInstance = _completedQuestTemplate.Instantiate();
                var container = itemInstance.Q<VisualElement>(className: "completed-item");
                
                var titleLabel = container.Q<Label>("TitleLabel");
                var descLabel = container.Q<Label>("DescLabel");

                titleLabel.text = quest.displayName;
                descLabel.text = quest.description;

                _listContainer.Add(itemInstance);
            }
        }

        private void OnQuestCompleted(QuestCompletedEvent evt)
        {
            // If the UI is open while a quest completes, just refresh the whole list
            RefreshList();
        }
    }
}
