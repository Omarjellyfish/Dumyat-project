using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// What each nail on the workbench does once it is driven home.
    ///
    /// The declaration ORDER of this enum is the serialized wiring: Unity stores the selection as an
    /// int, so moving an entry silently repoints every nail already placed in a scene. Continue is
    /// appended at the end for exactly that reason, even though it reads better beside NewGame.
    /// </summary>
    public enum MenuNailAction
    {
        NewGame,
        Settings,
        Credits,
        CloseSettings,
        CloseCredits,
        Quit,
        Continue
    }

    /// <summary>
    /// A menu nail. Starting, resuming and quitting go through <see cref="IGameSessionService"/> —
    /// the same service the UI Toolkit main menu's buttons use — so the two menus cannot drift apart
    /// about what New Game wipes or where Continue goes. Opening and closing the panels stays with
    /// <see cref="IMenuService"/>, which is the only thing that knows they exist.
    /// </summary>
    public class NailMenuAction : NailAction
    {
        [Header("Menu")]
        [SerializeField] private MenuNailAction _action;

        private IGameSessionService _session;
        private IMenuService _menuService;

        [Inject]
        private void Inject(IGameSessionService session, IMenuService menuService)
        {
            _session = session;
            _menuService = menuService;
        }

        /// <summary>
        /// Continue is the only nail that can be standing there and still refuse the hammer: there
        /// has to be a save to resume. This is the same rule the UI Toolkit Continue button enforces
        /// with SetEnabled, kept in one concept rather than two implementations.
        /// </summary>
        public override bool IsAvailable =>
            _action != MenuNailAction.Continue || (_session != null && _session.HasSave);

        private void OnEnable()
        {
            if (_session == null && RequiresSession(_action))
                Debug.LogWarning($"{nameof(NailMenuAction)} on '{name}' has no {nameof(IGameSessionService)} " +
                                 "injected — this nail will drive in and do nothing. Does this scene have " +
                                 "a ContainerScope?", this);
        }

        protected override void OnFullyDriven()
        {
            switch (_action)
            {
                // Driving the nail IS the confirmation. The UI Toolkit menu asks "start over?" in a
                // dialog because a button press is one twitch away from an accident; three
                // deliberate hammer blows are not, so there is nothing here to confirm.
                case MenuNailAction.NewGame:
                    _session?.NewGame();
                    break;

                case MenuNailAction.Continue:
                    _session?.Continue();
                    break;

                // Goes through the session service rather than Application.Quit so the save is
                // flushed on the way out, exactly as the main menu's Exit does.
                case MenuNailAction.Quit:
                    _session?.QuitApplication();
                    break;

                case MenuNailAction.Settings:
                    _menuService?.ToggleSettings();
                    break;

                case MenuNailAction.Credits:
                    _menuService?.ToggleCredits();
                    break;

                case MenuNailAction.CloseSettings:
                    _menuService?.CloseSettings();
                    break;

                case MenuNailAction.CloseCredits:
                    _menuService?.CloseCredits();
                    break;
            }
        }

        private static bool RequiresSession(MenuNailAction action) =>
            action == MenuNailAction.NewGame ||
            action == MenuNailAction.Continue ||
            action == MenuNailAction.Quit;
    }
}
