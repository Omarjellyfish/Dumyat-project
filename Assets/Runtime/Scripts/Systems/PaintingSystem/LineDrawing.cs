using UnityEngine;
using Reflex.Attributes;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    public class LineDrawing : MonoBehaviour
    {
        [SerializeField] private XRGrabInteractable _grabInteractable;
        [SerializeField] private PenTip _penTip;
        [SerializeField] private SurfaceConstraint _surfaceConstraint;
        [SerializeField] private Material _lineMaterial;
        [SerializeField] private float _lineWidth = 0.005f;
        private FloatingText _distanceText;
        [SerializeField] Vector3 offset;

        private LineRenderer _lineRenderer;
        private IFloatingTextService _floatingTextService;

        [Inject]
        // private void Inject(IFloatingTextService floatingTextService) => _floatingTextService = floatingTextService;

        private void Awake()
        {
            _grabInteractable = GetComponent<XRGrabInteractable>();

            if (_surfaceConstraint == null)
                _surfaceConstraint = GetComponent<SurfaceConstraint>();

            SetupLineRenderer();
        }

        private void OnEnable()
        {
            if (_grabInteractable == null)
            {
                Debug.LogWarning($"{nameof(LineDrawing)} on {name} needs an XRGrabInteractable.");
                return;
            }

            _grabInteractable.activated.AddListener(StartLineDraw);
            _grabInteractable.deactivated.AddListener(EndLineDraw);
        }

        private void OnDisable()
        {
            if (_grabInteractable == null)
                return;

            _grabInteractable.activated.RemoveListener(StartLineDraw);
            _grabInteractable.deactivated.RemoveListener(EndLineDraw);
        }

        private void SetupLineRenderer()
        {
            // FIX 1: Prevent crash if gameObject is null (due to illegal "new" instantiation)
            if (gameObject == null)
            {
                Debug.LogError($"[{nameof(LineDrawing)}] cannot setup. Script is not attached to an active GameObject!");
                return;
            }

            // FIX 2: Check if a LineRenderer already exists first to avoid stacking duplicates
            _lineRenderer = GetComponent<LineRenderer>();
            if (_lineRenderer == null)
            {
                _lineRenderer = gameObject.AddComponent<LineRenderer>();
            }

            if (_lineRenderer != null)
            {
                _lineRenderer.material = _lineMaterial;
                _lineRenderer.startWidth = _lineWidth;
                _lineRenderer.endWidth = _lineWidth;
                _lineRenderer.positionCount = 2;
                _lineRenderer.enabled = false;
            }
        }

        private bool _lineActive;

        private void EndLineDraw(DeactivateEventArgs args)
        {
            if (!_lineActive)
                return; 

            _lineActive = false;
            _penTip?.FinalizeLine();
            
            if (_lineRenderer != null)
                _lineRenderer.enabled = false;

            // if (_distanceText != null)
            {
                _floatingTextService?.Hide(_distanceText);
                _distanceText = null;
            }
        }

        private void StartLineDraw(ActivateEventArgs args)
        {
            if (_surfaceConstraint == null || !_surfaceConstraint.IsConstrained)
                return;

            _lineActive = true;
            if (_penTip != null)
                _penTip.CurrentMode = PenMode.LineDrawing;

            // FIX 3: Safe null-check before trying to enable the renderer
            if (_lineRenderer != null)
            {
                _lineRenderer.enabled = true;
            }
            else
            {
                Debug.LogError($"[{nameof(LineDrawing)}] LineRenderer is null. Cannot start drawing.");
            }

            // if (_floatingTextService != null)
            //     _distanceText = _floatingTextService.Show(transform.position, "0.0 cm");
        }

        public void UpdateLinePositions(Vector3 startWorldPoint, Vector3 currentWorldPoint)
        {
            if (_lineRenderer == null || !_lineRenderer.enabled)
                return;

            _lineRenderer.SetPosition(0, startWorldPoint);
            _lineRenderer.SetPosition(1, currentWorldPoint);

            Vector3 midpoint = (startWorldPoint + currentWorldPoint) / 2f;
            float distanceCm = Vector3.Distance(startWorldPoint, currentWorldPoint) * 100f;

            // if (_distanceText != null)
            // {
            //     _distanceText.SetPosition(midpoint, offset);
            //     _distanceText.SetText($"{distanceCm:F1} cm");
            // }
        }
    }
}