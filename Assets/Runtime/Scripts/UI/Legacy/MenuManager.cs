using UnityEngine;
using UnityEngine.SceneManagement;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public class MenuManager : MonoBehaviour, IMenuService
    {
        [Header("Panels")]
        [SerializeField] private GameObject _settingsPanel;
        [SerializeField] private GameObject _creditsPanel;

        [Header("Scene loading")]
        [SerializeField] private string _loadingSceneName = "";

        private ISceneLoadService _sceneLoadService;
        private IEventService _eventService;

        [Inject]
        private void Inject(ISceneLoadService sceneLoadService, IEventService eventService)
        {
            _sceneLoadService = sceneLoadService;
            _eventService = eventService;
        }

        private void Start()
        {
            PublishSettingsPanelState();
            PublishCreditsPanelState();
        }

        public void LoadLocation(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogWarning("MenuManager: Cannot load an empty scene name.");
                return;
            }

            if (_sceneLoadService != null)
                _sceneLoadService.LoadScene(sceneName, _loadingSceneName);
            else
                SceneManager.LoadScene(sceneName);
        }

        public void ToggleSettings() => SetSettingsOpen(_settingsPanel != null && !_settingsPanel.activeSelf);
        public void OpenSettings() => SetSettingsOpen(true);
        public void CloseSettings() => SetSettingsOpen(false);

        public void ToggleCredits() => SetCreditsOpen(_creditsPanel != null && !_creditsPanel.activeSelf);
        public void OpenCredits() => SetCreditsOpen(true);
        public void CloseCredits() => SetCreditsOpen(false);

        private void SetSettingsOpen(bool isOpen)
        {
            if (_settingsPanel == null) return;
            if (_settingsPanel.activeSelf == isOpen) return;

            _settingsPanel.SetActive(isOpen);
            if (isOpen) SetCreditsOpen(false);
            PublishSettingsPanelState();
        }

        private void SetCreditsOpen(bool isOpen)
        {
            if (_creditsPanel == null) return;
            if (_creditsPanel.activeSelf == isOpen) return;

            _creditsPanel.SetActive(isOpen);
            if (isOpen) SetSettingsOpen(false);
            PublishCreditsPanelState();
        }

        private void PublishSettingsPanelState()
        {
            if (_settingsPanel != null)
                _eventService?.Publish(new SettingsPanelToggledEvent(_settingsPanel.activeSelf));
        }

        private void PublishCreditsPanelState()
        {
            if (_creditsPanel != null)
                _eventService?.Publish(new CreditsPanelToggledEvent(_creditsPanel.activeSelf));
        }
    }
}