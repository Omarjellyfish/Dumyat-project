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
        private ISettingsService _settings;

        private VisualElement _root;
        private VisualElement _listContainer;

        private bool Arabic => UiLanguage.IsArabic(_settings);

        [Inject]
        public void Construct(IQuestService questService, IEventService eventService, ISettingsService settings)
        {
            _questService = questService;
            _eventService = eventService;
            _settings = settings;
        }

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            if (_root == null) return;

            _listContainer = _root.Q<VisualElement>("CompletedListContainer");

            if (_questService != null && _eventService != null)
            {
                RefreshList();
                _eventService.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
                _eventService.Subscribe<LanguageChangedEvent>(OnLanguageChanged);
            }
        }

        private void OnDisable()
        {
            if (_eventService != null)
            {
                _eventService.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
                _eventService.Unsubscribe<LanguageChangedEvent>(OnLanguageChanged);
            }
        }

        // Switching language with the log open would otherwise leave every row in the old language
        // until the next quest completed — which, for a list of finished quests, could be never.
        private void OnLanguageChanged(LanguageChangedEvent evt) => RefreshList();

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

                titleLabel.text = quest.DisplayNameFor(Arabic);
                descLabel.text = quest.DescriptionFor(Arabic);

                _listContainer.Add(itemInstance);
            }

            // Last, so every row above already carries its final string: ApplyScript walks the tree
            // and switches UI Toolkit's Arabic text generator on for whatever it finds.
            UiLanguage.ApplyScript(_root, Arabic);
        }

        private void OnQuestCompleted(QuestCompletedEvent evt)
        {
            // If the UI is open while a quest completes, just refresh the whole list
            RefreshList();
        }
    }
}
