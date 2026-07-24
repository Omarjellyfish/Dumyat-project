using UnityEngine;
using UnityEngine.UIElements;
using Reflex.Attributes;

namespace DumyatRuntime
{
    [RequireComponent(typeof(UIDocument))]
    public class AchievementsMenuUI : MonoBehaviour
    {
        [SerializeField] private VisualTreeAsset _achievementTemplate;
        
        [Tooltip("Text shown for descriptions of locked achievements")]
        [SerializeField] private string _lockedDescriptionText = "Keep playing to uncover this achievement.";

        private IAchievementService _achievementService;
        private IEventService _eventService;
        
        private VisualElement _gridContainer;

        [Inject]
        public void Construct(IAchievementService achievementService, IEventService eventService)
        {
            _achievementService = achievementService;
            _eventService = eventService;
        }

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null) return;

            _gridContainer = root.Q<VisualElement>("AchievementsGrid");

            if (_achievementService != null && _eventService != null)
            {
                RefreshList();
                _eventService.Subscribe<AchievementUnlockedEvent>(OnAchievementUnlocked);
            }
        }

        private void OnDisable()
        {
            if (_eventService != null)
            {
                _eventService.Unsubscribe<AchievementUnlockedEvent>(OnAchievementUnlocked);
            }
        }

        private void RefreshList()
        {
            if (_gridContainer == null || _achievementTemplate == null) return;
            
            _gridContainer.Clear();

            var allAchievements = _achievementService.GetAllAchievements();
            foreach (var achievement in allAchievements)
            {
                if (achievement == null) continue;

                var itemInstance = _achievementTemplate.Instantiate();
                var container = itemInstance.Q<VisualElement>(className: "achievement-item");
                
                var titleLabel = container.Q<Label>("TitleLabel");
                var descLabel = container.Q<Label>("DescLabel");
                var iconElement = container.Q<VisualElement>("IconElement");

                titleLabel.text = achievement.displayName;
                
                if (achievement.icon != null)
                {
                    iconElement.style.backgroundImage = new StyleBackground(achievement.icon);
                }

                bool isUnlocked = _achievementService.IsUnlocked(achievement.achievementID);
                if (isUnlocked)
                {
                    descLabel.text = achievement.description;
                    container.RemoveFromClassList("locked");
                }
                else
                {
                    descLabel.text = _lockedDescriptionText;
                    container.AddToClassList("locked");
                }

                _gridContainer.Add(itemInstance);
            }
        }

        private void OnAchievementUnlocked(AchievementUnlockedEvent evt)
        {
            // Simple refresh to show the new unlock
            RefreshList();
        }
    }
}
