using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Diagnostic. Logs every pointer event that reaches a world-space UI Toolkit panel, so
    /// "this button does nothing" can be split into the two very different faults it hides:
    ///
    /// • NOTHING is logged while you point at the panel → the pointer never reaches the panel at all.
    ///   The fault is upstream: the interactor, the collider, the layer, or the panel not being
    ///   registered for input. Nothing about the button or its handler can fix that.
    ///
    /// • Pointer events ARE logged but the button does not fire → the pointer arrives and the panel
    ///   routes it, and the fault is in the element under the cursor or in the click handler.
    ///
    /// Attach it to the same GameObject as the <see cref="UIDocument"/>, point at the panel, and read
    /// the Console. Put one on a panel that already works too — a control that logs nothing tells you
    /// the harness is wrong rather than the panel.
    ///
    /// Leave it off in a shipping build; it is a debugging aid, not a feature.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Dumyat/UI/World Panel Pointer Probe (Debug)")]
    public class WorldPanelPointerProbe : MonoBehaviour
    {
        [Tooltip("Log every PointerMove as well. Very noisy — turn it on only when you need to see " +
                 "whether the panel is tracking the pointer at all, not just entry and clicks.")]
        [SerializeField] private bool _logMoves;

        private UIDocument _document;
        private VisualElement _root;

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            _root = _document != null ? _document.rootVisualElement : null;
            if (_root == null)
            {
                Debug.LogWarning($"[PointerProbe] '{name}': no rootVisualElement to watch.", this);
                return;
            }

            // TrickleDown so the probe sees the event as it enters the panel, before any element has a
            // chance to stop it — otherwise a swallowed event looks identical to one that never arrived.
            _root.RegisterCallback<PointerEnterEvent>(OnEnter, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerLeaveEvent>(OnLeave, TrickleDown.TrickleDown);
            if (_logMoves) _root.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);

            Debug.Log($"[PointerProbe] watching '{name}'. Point at it and watch this Console.", this);
        }

        private void OnDisable()
        {
            if (_root == null) return;
            _root.UnregisterCallback<PointerEnterEvent>(OnEnter, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerLeaveEvent>(OnLeave, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
        }

        private void OnEnter(PointerEnterEvent e) => Log("ENTER", e.localPosition, e.target);
        private void OnDown(PointerDownEvent e) => Log("DOWN", e.localPosition, e.target);
        private void OnUp(PointerUpEvent e) => Log("UP", e.localPosition, e.target);
        private void OnLeave(PointerLeaveEvent e) => Log("LEAVE", e.localPosition, e.target);
        private void OnMove(PointerMoveEvent e) => Log("MOVE", e.localPosition, e.target);

        private void Log(string what, Vector3 local, IEventHandler target)
        {
            string hit = target is VisualElement ve
                ? (string.IsNullOrEmpty(ve.name) ? ve.GetType().Name : ve.name)
                : "?";
            Debug.Log($"[PointerProbe] {name}: {what} at {local} over '{hit}'", this);
        }
    }
}
