using Reflex.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DumyatRuntime
{
    /// <summary>
    /// Turns a button press into a <see cref="PauseToggleRequestedEvent"/>. That is the whole job.
    ///
    /// Splitting input away from <see cref="PauseMenuToolkitUI"/> is what lets a wrist button, a
    /// debug key or the XR Device Simulator open pause without the menu knowing any of them exist —
    /// and lets the binding be re-pointed without touching UI code.
    ///
    /// Keep exactly ONE of these live at a time. Two would publish twice per press and the menu
    /// would toggle straight back shut.
    /// </summary>
    public class PauseInputRelay : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Button action that opens/closes the pause menu — the UI/Menu action, bound to the " +
                 "left controller's menu button (and Escape for desk testing).")]
        [SerializeField] private InputActionReference _menuAction;

        private IEventService _eventService;

        [Inject]
        private void Inject(IEventService eventService) => _eventService = eventService;

        private void OnEnable()
        {
            if (_menuAction == null || _menuAction.action == null)
            {
                Debug.LogWarning($"{nameof(PauseInputRelay)} on '{name}' has no input action assigned — " +
                                 "the pause menu cannot be opened.", this);
                return;
            }

            _menuAction.action.performed += HandleMenuPressed;
            _menuAction.action.Enable();
        }

        private void OnDisable()
        {
            if (_menuAction == null || _menuAction.action == null) return;

            _menuAction.action.performed -= HandleMenuPressed;

            // Disable as well as unsubscribe: the action asset is a shared asset, so an action left
            // enabled by a destroyed object keeps polling its devices for the rest of the session.
            _menuAction.action.Disable();
        }

        private void HandleMenuPressed(InputAction.CallbackContext _)
        {
            if (_eventService == null)
            {
                Debug.LogWarning($"{nameof(PauseInputRelay)} on '{name}' has no {nameof(IEventService)} " +
                                 "injected — does this scene have a ContainerScope?", this);
                return;
            }

            _eventService.Publish(new PauseToggleRequestedEvent());
        }
    }
}
