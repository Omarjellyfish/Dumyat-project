using UnityEngine;
using UnityEngine.Serialization;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// Shows the line-drawing preview - a LineRenderer between the pen's first contact
    /// point and where the tip is now, plus a floating length readout - and commits the
    /// line to the wood when the player lets go.
    /// </summary>
    /// <remarks>
    /// Driven entirely by <see cref="SurfaceConstraintChangedEvent"/> rather than by the
    /// grab interactable's own activate/deactivate events. Both this component and
    /// <see cref="SurfaceConstraint"/> used to listen to <c>activated</c> directly, and
    /// this one is ordered first on the pen, so it always ran while
    /// <see cref="SurfaceConstraint.IsConstrained"/> was still false and bailed out -
    /// the trigger appeared to do nothing at all. Reacting to the constraint's own
    /// state change removes the ordering assumption completely.
    /// </remarks>
    public class LineDrawing : MonoBehaviour
    {
        [SerializeField] private PenTip _penTip;
        [SerializeField] private SurfaceConstraint _surfaceConstraint;
        [SerializeField] private Material _lineMaterial;
        [SerializeField] private float _lineWidth = 0.005f;
        [FormerlySerializedAs("offset")]
        [SerializeField] private Vector3 _distanceTextOffset;

        private LineRenderer _lineRenderer;
        private FloatingText _distanceText;
        private bool _lineActive;

        private IFloatingTextService _floatingTextService;
        private IEventService _eventService;

        [Inject]
        private void Inject(IFloatingTextService floatingTextService, IEventService eventService)
        {
            _floatingTextService = floatingTextService;
            _eventService = eventService;
        }

        private void Awake()
        {
            if (_surfaceConstraint == null)
                _surfaceConstraint = GetComponent<SurfaceConstraint>();

            if (_penTip == null)
                _penTip = GetComponentInChildren<PenTip>();

            SetupLineRenderer();
        }

        private void OnEnable()
        {
            if (_surfaceConstraint == null)
            {
                Debug.LogWarning(
                    $"{nameof(LineDrawing)} on {name} needs a {nameof(SurfaceConstraint)}; " +
                    "line drawing is disabled.", this);
                return;
            }

            if (_eventService == null)
            {
                Debug.LogWarning(
                    $"{nameof(LineDrawing)} on {name} has no {nameof(IEventService)} injected, so it " +
                    "cannot react to the pen touching a surface and line drawing will not start. " +
                    "Does this scene have a ContainerScope?", this);
                return;
            }

            _eventService.Subscribe<SurfaceConstraintChangedEvent>(OnConstraintChanged);
        }

        private void OnDisable()
        {
            _eventService?.Unsubscribe<SurfaceConstraintChangedEvent>(OnConstraintChanged);

            if (_lineActive)
                EndLineDraw();
        }

        private void OnConstraintChanged(SurfaceConstraintChangedEvent evt)
        {
            if (evt.Constraint != _surfaceConstraint)
                return;

            if (evt.IsConstrained)
                StartLineDraw();
            else
                EndLineDraw();
        }

        private void SetupLineRenderer()
        {
            _lineRenderer = GetComponent<LineRenderer>();
            if (_lineRenderer == null)
                _lineRenderer = gameObject.AddComponent<LineRenderer>();

            _lineRenderer.material = _lineMaterial;
            _lineRenderer.startWidth = _lineWidth;
            _lineRenderer.endWidth = _lineWidth;
            _lineRenderer.positionCount = 2;
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.enabled = false;
        }

        private void StartLineDraw()
        {
            if (_lineActive)
                return;

            _lineActive = true;

            if (_penTip != null)
                _penTip.CurrentMode = PenMode.LineDrawing;

            if (_lineRenderer != null)
                _lineRenderer.enabled = true;

            if (_floatingTextService != null)
                _distanceText = _floatingTextService.Show(transform.position, "0.0 cm");
        }

        private void EndLineDraw()
        {
            if (!_lineActive)
                return;

            _lineActive = false;
            _penTip?.FinalizeLine();

            if (_lineRenderer != null)
                _lineRenderer.enabled = false;

            HideDistanceText();
        }

        public void UpdateLinePositions(Vector3 startWorldPoint, Vector3 currentWorldPoint)
        {
            if (_lineRenderer == null || !_lineRenderer.enabled)
                return;

            _lineRenderer.SetPosition(0, startWorldPoint);
            _lineRenderer.SetPosition(1, currentWorldPoint);

            Vector3 midpoint = (startWorldPoint + currentWorldPoint) / 2f;
            float distanceCm = Vector3.Distance(startWorldPoint, currentWorldPoint) * 100f;

            if (_distanceText != null)
            {
                _distanceText.SetPosition(midpoint, _distanceTextOffset);
                _distanceText.SetText($"{distanceCm:F1} cm");
            }
            else if (_floatingTextService != null)
            {
                _distanceText = _floatingTextService.Show(midpoint, $"{distanceCm:F1} cm");
            }
        }

        private void HideDistanceText()
        {
            if (_distanceText == null)
                return;

            _floatingTextService?.Hide(_distanceText);
            _distanceText = null;
        }
    }
}
