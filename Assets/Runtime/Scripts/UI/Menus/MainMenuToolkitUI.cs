using System;
using System.Globalization;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// The main menu panel: New Game, Continue, Settings, Exit.
    ///
    /// It owns presentation only — what those buttons actually mean lives in
    /// <see cref="IGameSessionService"/>, so the pause menu's "Exit to Main Menu" and this panel's
    /// "Continue" cannot drift apart.
    /// </summary>
    public class MainMenuToolkitUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private UIDocument _uiDocument;

        [Tooltip("Shared settings page controller on this GameObject. Auto-resolved if empty.")]
        [SerializeField] private SettingsToolkitUI _settingsUI;

        private IGameSessionService _session;
        private IEventService _eventService;

        [Inject]
        private void Inject(IGameSessionService session, IEventService eventService)
        {
            _session = session;
            _eventService = eventService;
        }

        private VisualElement _panelRoot;
        private VisualElement _menuRoot;
        private Button _continueButton;
        private Label _continueSubLabel;
        private MenuConfirmOverlay _confirm;

        private const string DisabledButtonClass = "menu-btn--disabled";

        private void OnEnable()
        {
            if (_uiDocument == null) _uiDocument = GetComponent<UIDocument>();
            if (_settingsUI == null) _settingsUI = GetComponent<SettingsToolkitUI>();

            if (_uiDocument == null)
            {
                Debug.LogWarning($"{nameof(MainMenuToolkitUI)} on '{name}' has no UIDocument — " +
                                 "the main menu will not appear.", this);
                return;
            }

            _panelRoot = _uiDocument.rootVisualElement;
            if (_panelRoot == null) return;

            _menuRoot = _panelRoot.Q<VisualElement>("MenuRoot");
            _confirm = new MenuConfirmOverlay(_panelRoot);

            if (_session == null)
                Debug.LogWarning($"{nameof(MainMenuToolkitUI)} on '{name}' has no {nameof(IGameSessionService)} " +
                                 "injected — the buttons will do nothing. Does this scene have a ContainerScope?", this);

            BindButtons();
            SetVersionLabel();
            RefreshContinueButton();
            ShowMenuPage();

            if (_settingsUI != null) _settingsUI.BackRequested += ShowMenuPage;

            _eventService?.Subscribe<SceneLoadBeginEvent>(HandleSceneLoadBegin);
        }

        private void OnDisable()
        {
            if (_settingsUI != null) _settingsUI.BackRequested -= ShowMenuPage;

            _eventService?.Unsubscribe<SceneLoadBeginEvent>(HandleSceneLoadBegin);
        }

        /// <summary>
        /// Drops the panel the moment a load starts, the same way the pause menu does.
        ///
        /// This is a visibility fix, not a tidiness one: the menu is a fixed 900 x 620 world-space
        /// panel with no lazy-follow, while the loading overlay floats 1.2 m in front of the head. A
        /// player standing closer to the menu board than that had the loading panel rendered behind
        /// it, which is why it looked like nothing happened after pressing New Game.
        /// </summary>
        private void HandleSceneLoadBegin(SceneLoadBeginEvent _) => SetPanelVisible(false);

        private void SetPanelVisible(bool visible)
        {
            if (_panelRoot == null) return;

            if (!visible)
            {
                _confirm?.Hide();
                _settingsUI?.Close();
            }

            _panelRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void BindButtons()
        {
            Button newGame = _panelRoot.Q<Button>("NewGameButton");
            if (newGame != null) newGame.clicked += HandleNewGameClicked;

            _continueButton = _panelRoot.Q<Button>("ContinueButton");
            _continueSubLabel = _panelRoot.Q<Label>("ContinueSubLabel");
            if (_continueButton != null) _continueButton.clicked += () => _session?.Continue();

            Button settings = _panelRoot.Q<Button>("SettingsButton");
            if (settings != null) settings.clicked += ShowSettingsPage;

            Button exit = _panelRoot.Q<Button>("ExitButton");
            if (exit != null)
            {
                exit.clicked += () => _confirm?.Ask(
                    "QUIT DUMYAT?",
                    "Your progress is saved before the game closes.",
                    () => _session?.QuitApplication());
            }
        }

        /// <summary>
        /// A fresh start is only destructive if there is something to destroy — so it confirms when
        /// a save exists and starts straight away when one does not.
        /// </summary>
        private void HandleNewGameClicked()
        {
            if (_session == null) return;

            if (!_session.HasSave)
            {
                _session.NewGame();
                return;
            }

            _confirm?.Ask(
                "START OVER?",
                "Your current save will be erased. This cannot be undone.",
                () => _session.NewGame());
        }

        private void RefreshContinueButton()
        {
            if (_continueButton == null) return;

            bool hasSave = _session != null && _session.HasSave;

            // SetEnabled is what actually blocks the click; the class only dims it.
            _continueButton.SetEnabled(hasSave);
            _continueButton.EnableInClassList(DisabledButtonClass, !hasSave);

            if (_continueSubLabel == null) return;

            _continueSubLabel.text = hasSave
                ? BuildResumeDescription(_session.ResumeSceneName, _session.ResumeSavedAtUtc)
                : "No saved game";
        }

        /// <summary>Scene name, plus how long ago it was left when that can be worked out.</summary>
        private static string BuildResumeDescription(string sceneName, string savedAtUtc)
        {
            if (string.IsNullOrEmpty(savedAtUtc)) return sceneName;

            if (!DateTime.TryParse(savedAtUtc, CultureInfo.InvariantCulture,
                                   DateTimeStyles.RoundtripKind, out DateTime saved))
                return sceneName;

            TimeSpan ago = DateTime.UtcNow - saved;

            // A clock skew or an edited save can put the stamp in the future; showing the scene
            // alone beats showing "in -3 hours".
            if (ago < TimeSpan.Zero) return sceneName;

            if (ago.TotalMinutes < 2) return $"{sceneName} · just now";
            if (ago.TotalHours < 1) return $"{sceneName} · {(int)ago.TotalMinutes} min ago";
            if (ago.TotalDays < 1) return $"{sceneName} · {(int)ago.TotalHours} h ago";

            return $"{sceneName} · {(int)ago.TotalDays} d ago";
        }

        private void SetVersionLabel()
        {
            Label version = _panelRoot.Q<Label>("VersionLabel");
            if (version != null) version.text = "v" + Application.version;
        }

        private void ShowMenuPage()
        {
            _confirm?.Hide();
            _settingsUI?.Close();
            if (_menuRoot != null) _menuRoot.style.display = DisplayStyle.Flex;

            // Re-read on the way back: a New Game confirmed and cancelled, or a save written while
            // the settings page was open, would otherwise leave Continue showing stale text.
            RefreshContinueButton();
        }

        private void ShowSettingsPage()
        {
            if (_menuRoot != null) _menuRoot.style.display = DisplayStyle.None;
            _settingsUI?.Open();
        }
    }
}
