using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// The in-game pause panel: Resume, Settings, Exit to Main Menu.
    ///
    /// Pausing goes through <see cref="IGameStateService"/> rather than setting
    /// <c>Time.timeScale</c> here — the state service already owns timeScale, and routing through
    /// it means anything listening for <see cref="GameStateChangedEvent"/> learns about the pause
    /// too. <see cref="GamePauseToggledEvent"/> is published alongside it for existing listeners.
    ///
    /// Lives per gameplay scene, like the inventory panel: a scene-resident object gets normal
    /// Reflex injection from the scene's ContainerScope, which a DontDestroyOnLoad one would not.
    /// </summary>
    public class PauseMenuToolkitUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private UIDocument _uiDocument;

        [Tooltip("Shared settings page controller on this GameObject. Auto-resolved if empty.")]
        [SerializeField] private SettingsToolkitUI _settingsUI;

        [Tooltip("Optional. Snapped in front of the player each time the menu opens.")]
        [SerializeField] private LazyFollowAnchor _anchor;

        [Header("Display")]
        [Tooltip("Shown next to the PAUSED title. Falls back to the scene name if empty.")]
        [SerializeField] private string _locationDisplayName = "";

        private IEventService _eventService;
        private IGameStateService _gameState;
        private IGameSessionService _session;
        private ISceneLoadService _sceneLoad;

        [Inject]
        private void Inject(IEventService eventService, IGameStateService gameState,
                            IGameSessionService session, ISceneLoadService sceneLoad)
        {
            _eventService = eventService;
            _gameState = gameState;
            _session = session;
            _sceneLoad = sceneLoad;
        }

        private VisualElement _panelRoot;
        private VisualElement _menuRoot;
        private MenuConfirmOverlay _confirm;

        public bool IsOpen { get; private set; }

        private void OnEnable()
        {
            if (_uiDocument == null) _uiDocument = GetComponent<UIDocument>();
            if (_settingsUI == null) _settingsUI = GetComponent<SettingsToolkitUI>();
            if (_anchor == null) _anchor = GetComponent<LazyFollowAnchor>();

            if (_uiDocument == null)
            {
                Debug.LogWarning($"{nameof(PauseMenuToolkitUI)} on '{name}' has no UIDocument — " +
                                 "the pause menu will not appear.", this);
                return;
            }

            _panelRoot = _uiDocument.rootVisualElement;
            if (_panelRoot == null) return;

            _menuRoot = _panelRoot.Q<VisualElement>("PauseRoot");
            _confirm = new MenuConfirmOverlay(_panelRoot);

            BindButtons();
            SetLocationLabel();

            // Closed, and closed silently: Close() would publish an "unpaused" event and force
            // FreeRoaming on a scene that may legitimately have started in another state.
            SetPanelVisible(false);
            _settingsUI?.Close();
            _confirm?.Hide();

            if (_settingsUI != null) _settingsUI.BackRequested += ShowMenuPage;

            if (_eventService == null)
            {
                Debug.LogWarning($"{nameof(PauseMenuToolkitUI)} on '{name}' has no {nameof(IEventService)} " +
                                 "injected — the menu button will not open it. Does this scene have a " +
                                 "ContainerScope?", this);
                return;
            }

            _eventService.Subscribe<PauseToggleRequestedEvent>(HandleToggleRequested);
        }

        private void OnDisable()
        {
            if (_settingsUI != null) _settingsUI.BackRequested -= ShowMenuPage;
            _eventService?.Unsubscribe<PauseToggleRequestedEvent>(HandleToggleRequested);
        }

        private void BindButtons()
        {
            Button resume = _panelRoot.Q<Button>("ResumeButton");
            if (resume != null) resume.clicked += Close;

            Button settings = _panelRoot.Q<Button>("SettingsButton");
            if (settings != null) settings.clicked += ShowSettingsPage;

            Button exit = _panelRoot.Q<Button>("ExitToMenuButton");
            if (exit != null)
            {
                exit.clicked += () => _confirm?.Ask(
                    "LEAVE THE GAME?",
                    "You will return to the main menu. Your progress is already saved.",
                    () => _session?.ExitToMainMenu());
            }
        }

        private void SetLocationLabel()
        {
            Label label = _panelRoot.Q<Label>("PauseSubLabel");
            if (label == null) return;

            label.text = string.IsNullOrWhiteSpace(_locationDisplayName)
                ? UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.ToUpperInvariant()
                : _locationDisplayName.ToUpperInvariant();
        }

        private void HandleToggleRequested(PauseToggleRequestedEvent _)
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (IsOpen || _panelRoot == null) return;

            // Nothing to pause on the main menu, and opening mid-load would freeze the loading
            // overlay's own transition behind a panel the player cannot dismiss.
            if (_gameState != null && _gameState.CurrentState == GameState.MainMenu) return;
            if (_sceneLoad != null && _sceneLoad.IsLoading) return;

            IsOpen = true;
            SetPanelVisible(true);
            ShowMenuPage();

            // Snap to the current view rather than easing in from wherever the player last left it.
            _anchor?.Reanchor();

            // SetState is what actually zeroes Time.timeScale.
            _gameState?.SetState(GameState.Paused);
            _eventService?.Publish(new GamePauseToggledEvent(true));
        }

        public void Close()
        {
            if (!IsOpen || _panelRoot == null) return;

            IsOpen = false;
            SetPanelVisible(false);

            _gameState?.SetState(GameState.FreeRoaming);
            _eventService?.Publish(new GamePauseToggledEvent(false));
        }

        private void ShowMenuPage()
        {
            _confirm?.Hide();
            _settingsUI?.Close();
            if (_menuRoot != null) _menuRoot.style.display = DisplayStyle.Flex;
        }

        private void ShowSettingsPage()
        {
            if (_menuRoot != null) _menuRoot.style.display = DisplayStyle.None;
            _settingsUI?.Open();
        }

        private void SetPanelVisible(bool visible)
        {
            if (_panelRoot != null)
                _panelRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
