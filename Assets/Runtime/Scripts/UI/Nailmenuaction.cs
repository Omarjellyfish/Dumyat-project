using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public enum MenuNailAction
    {
        Start,
        ToggleSettings,
        ToggleCredits,
        CloseSettings,
        CloseCredits,
        Quit
    }

    public class NailMenuAction : NailAction
    {
        [SerializeField] private MenuNailAction _action;

        [Tooltip("Only used when Action is Start.")]
        [SerializeField] private string _firstRoomSceneName;

        private IMenuService _menuService;

        [Inject]
        private void Inject(IMenuService menuService)
        {
            _menuService = menuService;
        }

        protected override void OnFullyDriven()
        {
            if (_action == MenuNailAction.Quit)
            {
                QuitGame();
                return;
            }

            if (_menuService == null) return;

            switch (_action)
            {
                case MenuNailAction.Start:
                    _menuService.LoadLocation(_firstRoomSceneName);
                    break;
                case MenuNailAction.ToggleSettings:
                    _menuService.ToggleSettings();
                    break;
                case MenuNailAction.ToggleCredits:
                    _menuService.ToggleCredits();
                    break;
                case MenuNailAction.CloseSettings:
                    _menuService.CloseSettings();
                    break;
                case MenuNailAction.CloseCredits:
                    _menuService.CloseCredits();
                    break;
            }
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}