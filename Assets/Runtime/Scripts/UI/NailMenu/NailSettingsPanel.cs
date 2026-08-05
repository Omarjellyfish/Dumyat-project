using System.Collections;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Hangs the game's shared settings page on the carpentry menu's wall.
    ///
    /// The nail menu used to carry its own uGUI settings panel, which reached three of the mixer's
    /// five channels and had no controls page. Rather than growing that panel, this hosts the same
    /// <see cref="SettingsToolkitUI"/> the main menu and the pause menu use, so there is one settings
    /// screen in the game instead of two that resemble each other.
    ///
    /// <see cref="IMenuService"/> shows and hides the GameObject this sits on; all this component
    /// does is open the page inside it and route the page's own BACK button to the same close the
    /// Back NAIL performs, so both ways out agree.
    /// </summary>
    public class NailSettingsPanel : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The shared settings page. Found on this GameObject or a child when left empty.")]
        [SerializeField] private SettingsToolkitUI _settingsUI;

        private IMenuService _menuService;
        private Coroutine _opening;

        [Inject]
        private void Inject(IMenuService menuService) => _menuService = menuService;

        private void OnEnable()
        {
            if (_settingsUI == null) _settingsUI = GetComponentInChildren<SettingsToolkitUI>(true);

            if (_settingsUI == null)
            {
                Debug.LogWarning($"{nameof(NailSettingsPanel)} on '{name}' found no {nameof(SettingsToolkitUI)} — " +
                                 "the Settings nail will open an empty panel.", this);
                return;
            }

            _settingsUI.BackRequested += Close;
            _opening = StartCoroutine(OpenWhenBound());
        }

        private void OnDisable()
        {
            if (_opening != null)
            {
                StopCoroutine(_opening);
                _opening = null;
            }

            if (_settingsUI == null) return;

            _settingsUI.BackRequested -= Close;
            _settingsUI.Close();
        }

        /// <summary>
        /// Waits one frame before opening.
        ///
        /// <see cref="SettingsToolkitUI"/> binds its elements in its own OnEnable and then closes
        /// itself, and Unity gives no ordering guarantee between two components waking on the same
        /// frame. Opening immediately therefore lands either before the page exists (nothing happens)
        /// or before it closes itself (it shuts again straight away) depending on component order.
        /// A frame's wait puts this after both, every time.
        /// </summary>
        private IEnumerator OpenWhenBound()
        {
            yield return null;

            _settingsUI.Open();
            _opening = null;
        }

        private void Close() => _menuService?.CloseSettings();
    }
}
