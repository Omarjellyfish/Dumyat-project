using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Reflex.Attributes;

namespace DumyatRuntime
{
    [RequireComponent(typeof(UIDocument))]
    public class WristQuestTrackerUI : MonoBehaviour
    {
        [SerializeField] private VisualTreeAsset _questItemTemplate;

        private IQuestService _questService;
        private IEventService _eventService;
        
        private VisualElement _listContainer;
        private Dictionary<string, VisualElement> _activeQuestElements = new Dictionary<string, VisualElement>();

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

            _listContainer = root.Q<VisualElement>("QuestListContainer");

            if (_questService != null && _eventService != null)
            {
                InitializeList();
                SubscribeToEvents();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromEvents();
        }

        private void SubscribeToEvents()
        {
            _eventService.Subscribe<QuestActivatedEvent>(OnQuestActivated);
            _eventService.Subscribe<QuestProgressedEvent>(OnQuestProgressed);
            _eventService.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        private void UnsubscribeFromEvents()
        {
            if (_eventService != null)
            {
                _eventService.Unsubscribe<QuestActivatedEvent>(OnQuestActivated);
                _eventService.Unsubscribe<QuestProgressedEvent>(OnQuestProgressed);
                _eventService.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
            }
        }

        private void InitializeList()
        {
            _listContainer.Clear();
            _activeQuestElements.Clear();

            var activeQuests = _questService.GetActiveQuests();
            foreach (var quest in activeQuests)
            {
                AddQuestElement(quest);
            }
        }

        private void AddQuestElement(QuestDefinitionSO def)
        {
            if (_questItemTemplate == null) return;
            if (_activeQuestElements.ContainsKey(def.questID)) return;

            var itemInstance = _questItemTemplate.Instantiate();
            var container = itemInstance.Q<VisualElement>(className: "quest-item");
            
            var titleLabel = container.Q<Label>("TitleLabel");
            var progressLabel = container.Q<Label>("ProgressLabel");
            var progressBar = container.Q<ProgressBar>("ProgressBar");

            titleLabel.text = def.displayName;
            
            int currentProgress = _questService.GetProgress(def.questID);
            progressLabel.text = $"{currentProgress}/{def.targetCount}";
            
            progressBar.highValue = def.targetCount;
            progressBar.value = currentProgress;

            _listContainer.Add(itemInstance);
            _activeQuestElements.Add(def.questID, itemInstance);
        }

        private void OnQuestActivated(QuestActivatedEvent evt)
        {
            var def = _questService.GetDefinition(evt.QuestID);
            if (def != null)
            {
                AddQuestElement(def);
            }
        }

        private void OnQuestProgressed(QuestProgressedEvent evt)
        {
            if (_activeQuestElements.TryGetValue(evt.QuestID, out var element))
            {
                var progressLabel = element.Q<Label>("ProgressLabel");
                var progressBar = element.Q<ProgressBar>("ProgressBar");

                progressLabel.text = $"{evt.CurrentProgress}/{evt.TargetCount}";
                progressBar.value = evt.CurrentProgress;
            }
        }

        private void OnQuestCompleted(QuestCompletedEvent evt)
        {
            if (_activeQuestElements.TryGetValue(evt.QuestID, out var element))
            {
                _listContainer.Remove(element);
                _activeQuestElements.Remove(evt.QuestID);
            }
        }
    }
}
