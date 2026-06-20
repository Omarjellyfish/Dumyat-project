using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    public class LineDrawing : MonoBehaviour
    {
        [SerializeField] private XRGrabInteractable _grabInteractable;
        [SerializeField] private PenTip _penTip;
        [SerializeField] private Material _lineMaterial;
        [SerializeField] private float _lineWidth = 0.005f;

        private LineRenderer _lineRenderer;

        private void Awake()
        {
            _grabInteractable = GetComponent<XRGrabInteractable>();
            SetupLineRenderer();
        }

        private void OnEnable()
        {
            _grabInteractable.activated.AddListener(StartLineDraw);
            _grabInteractable.deactivated.AddListener(EndLineDraw);
        }

        private void OnDisable()
        {
            _grabInteractable.activated.RemoveListener(StartLineDraw);
            _grabInteractable.deactivated.RemoveListener(EndLineDraw);
        }

        private void SetupLineRenderer()
        {
            _lineRenderer = gameObject.AddComponent<LineRenderer>();
            _lineRenderer.material = _lineMaterial;
            _lineRenderer.startWidth = _lineWidth;
            _lineRenderer.endWidth = _lineWidth;
            _lineRenderer.positionCount = 2;
            _lineRenderer.enabled = false; 
        }

        private void StartLineDraw(ActivateEventArgs args)
        {
            _penTip._currentMode = PenMode.LineDrawing;
            _lineRenderer.enabled = true;
        }

        private void EndLineDraw(DeactivateEventArgs args)
        {
            _penTip.ExitLineDrawingMode() ;
            _lineRenderer.enabled = false;
        }

        public void UpdateLinePositions(Vector3 startWorldPoint, Vector3 currentWorldPoint)
        {
            _lineRenderer.SetPosition(0, startWorldPoint);
            _lineRenderer.SetPosition(1, currentWorldPoint);
        }
    }
}