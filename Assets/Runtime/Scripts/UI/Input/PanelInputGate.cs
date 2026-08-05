using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Keeps a UI Toolkit panel out of the pointer contest while it is hidden.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// <see cref="PanelRaycaster"/> is a <c>BaseRaycaster</c>, and the EventSystem asks EVERY enabled
    /// raycaster in the scene which one is under the pointer before it delivers a click. Hiding a panel
    /// the normal way — <c>root.style.display = None</c>, as <see cref="InventoryToolkitUI"/> does —
    /// hides the CONTENT but leaves the GameObject active, so its raycaster keeps competing for every
    /// pointer event in the game.
    ///
    /// That is harmless for a panel bolted to the world. It is not harmless for one parented to the
    /// player: the inventory panel rides on the XR rig, so it travels with the head and sits
    /// permanently between the player and whatever they are aiming at. It wins the pointer, the click
    /// is delivered to a panel showing nothing, and the world panel the player was actually pointing at
    /// never hears it. The symptom is that every world-space menu in every scene stops being clickable
    /// while the ray, the trigger and the interactor all keep reporting perfectly healthy — the hand-off
    /// from XRI is correct, and the loss happens one step later in deciding who owns the pointer.
    ///
    /// Gating on the resolved display rather than subscribing to a show/hide event is deliberate: it
    /// costs one style read per frame and cannot be bypassed, whereas an event has to be raised by every
    /// present and future code path that hides the panel, and the one that forgets is silent.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Dumyat/UI/Panel Input Gate")]
    public class PanelInputGate : MonoBehaviour
    {
        private UIDocument _document;
        private PanelRaycaster _raycaster;
        private PanelEventHandler _eventHandler;

        /// <summary>Tracks the last applied state so the common frame does no work at all.</summary>
        private bool _isOpen = true;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _raycaster = GetComponent<PanelRaycaster>();
            _eventHandler = GetComponent<PanelEventHandler>();

            if (_raycaster == null && _eventHandler == null)
                Debug.LogWarning($"{nameof(PanelInputGate)} on '{name}' found no {nameof(PanelRaycaster)} " +
                                 $"or {nameof(PanelEventHandler)} to gate. Unity adds those to a panel that " +
                                 "takes pointer input, so this component has nothing to do here.", this);
        }

        // Both components are created by UI Toolkit rather than authored, so they may not exist yet when
        // this wakes. Re-resolving here costs nothing and covers a panel that gains them later.
        private void OnEnable() => Apply(IsShowing(), force: true);

        private void LateUpdate()
        {
            bool showing = IsShowing();
            if (showing == _isOpen) return;

            Apply(showing, force: false);
        }

        /// <summary>
        /// A panel laid out to nothing cannot be pointed at, so it must not claim to be. Reading the
        /// RESOLVED display catches a panel hidden by a stylesheet as well as one hidden from code.
        /// </summary>
        private bool IsShowing()
        {
            VisualElement root = _document != null ? _document.rootVisualElement : null;
            return root != null && root.resolvedStyle.display != DisplayStyle.None;
        }

        private void Apply(bool showing, bool force)
        {
            _isOpen = showing;

            if (_raycaster == null) _raycaster = GetComponent<PanelRaycaster>();
            if (_eventHandler == null) _eventHandler = GetComponent<PanelEventHandler>();

            if (_raycaster != null && (force || _raycaster.enabled != showing))
                _raycaster.enabled = showing;

            if (_eventHandler != null && (force || _eventHandler.enabled != showing))
                _eventHandler.enabled = showing;
        }
    }
}
