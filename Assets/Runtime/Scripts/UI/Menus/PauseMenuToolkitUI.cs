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
    /// ONE persistent instance serves every scene. It is spawned and injected by
    /// <see cref="PauseMenuPresenter"/> (a root-container service) and marked DontDestroyOnLoad, so
    /// it survives scene loads and no scene carries its own copy — do not place this prefab in a
    /// scene as well. Everything scene-specific is therefore resolved per-open, not cached in
    /// <c>OnEnable</c>: the location label is read each time the panel opens, and the state to return
    /// to on Resume is whatever the game was in when it was paused.
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
        [Tooltip("Shown next to the PAUSED title. Leave EMPTY: this menu is shared by every scene, so " +
                 "a fixed name here would be wrong everywhere but one. Empty uses the active scene name.")]
        [SerializeField] private string _locationDisplayName = "";

        private IEventService _eventService;
        private IGameStateService _gameState;
        private IGameSessionService _session;
        private ISceneLoadService _sceneLoad;
        private ISettingsService _settings;

        [Inject]
        private void Inject(IEventService eventService, IGameStateService gameState,
                            IGameSessionService session, ISceneLoadService sceneLoad,
                            ISettingsService settings)
        {
            _eventService = eventService;
            _gameState = gameState;
            _session = session;
            _sceneLoad = sceneLoad;
            _settings = settings;
        }

        private VisualElement _panelRoot;
        private VisualElement _menuRoot;
        private MenuConfirmOverlay _confirm;

        // What to put the game back into on Resume. Defaults to FreeRoaming for the case where there
        // is no IGameStateService to ask.
        private GameState _stateBeforePause = GameState.FreeRoaming;

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

            // This instance outlives the scene it was opened in, so a load starting underneath it
            // has to shut it down — otherwise the panel would still be up over the next scene.
            _eventService.Subscribe<SceneLoadBeginEvent>(HandleSceneLoadBegin);
        }

        private void OnDisable()
        {
            if (_settingsUI != null) _settingsUI.BackRequested -= ShowMenuPage;
            _eventService?.Unsubscribe<PauseToggleRequestedEvent>(HandleToggleRequested);
            _eventService?.Unsubscribe<SceneLoadBeginEvent>(HandleSceneLoadBegin);
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

        /// <summary>
        /// Names the place the player is standing in.
        ///
        /// The scene name is the last resort, not the first: "RasElBar" and "EzbetElBorg" are build
        /// artefacts, and the towns already have proper names — in both languages — on the destination
        /// assets the bus board reads from. Pausing should call the place what the departures board
        /// called it half a minute ago.
        /// </summary>
        private void SetLocationLabel()
        {
            Label label = _panelRoot.Q<Label>("PauseSubLabel");
            if (label == null) return;

            string name = !string.IsNullOrWhiteSpace(_locationDisplayName)
                ? _locationDisplayName
                : SceneDisplayName.ForActiveScene(UiLanguage.IsArabic(_settings));

            // Arabic has no case, and ToUpperInvariant on it is a no-op that would only risk mangling
            // a shaped string — so the shout is applied to Latin text only.
            label.text = UiLanguage.IsArabic(_settings) ? name : name.ToUpperInvariant();
        }

        private void HandleToggleRequested(PauseToggleRequestedEvent _)
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>
        /// Drops the panel without touching game state — whoever started the load already owns the
        /// state it is heading into (ExitToMainMenu sets MainMenu before loading, for instance).
        /// The one thing that must not survive is a zeroed timeScale, so Paused is unwound here.
        /// </summary>
        private void HandleSceneLoadBegin(SceneLoadBeginEvent _)
        {
            if (!IsOpen) return;

            IsOpen = false;
            SetPanelVisible(false);
            _settingsUI?.Close();
            _confirm?.Hide();

            if (_gameState != null && _gameState.CurrentState == GameState.Paused)
                _gameState.SetState(_stateBeforePause);

            _eventService?.Publish(new GamePauseToggledEvent(false));
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

            // Read per-open, not cached in OnEnable: this instance persists across scene loads, so
            // the scene it is floating in is not the one it was created in.
            SetLocationLabel();

            // Snap to the current view rather than easing in from wherever the player last left it —
            // which after a scene load is a stale pose from the previous scene. (The anchor picks up
            // the new scene's Camera.main on its own once the old one is destroyed.)
            _anchor?.Reanchor();

            // Remembered so Resume returns to whatever the player was doing — pausing mid-quest and
            // resuming must not silently drop them back to FreeRoaming.
            if (_gameState != null) _stateBeforePause = _gameState.CurrentState;

            // SetState is what actually zeroes Time.timeScale.
            _gameState?.SetState(GameState.Paused);
            _eventService?.Publish(new GamePauseToggledEvent(true));
        }

        public void Close()
        {
            if (!IsOpen || _panelRoot == null) return;

            IsOpen = false;
            SetPanelVisible(false);

            _gameState?.SetState(_stateBeforePause);
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
