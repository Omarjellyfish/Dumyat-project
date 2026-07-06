using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public class PenTip : MonoBehaviour
    {
        [Header("Detection Settings")]
        [SerializeField] private LayerMask _woodLayer;
        [SerializeField] private float _rayDistance = 0.05f;
        [SerializeField] private PaintBrush _paintBrush;
        [SerializeField] private LineDrawing _lineDraw;
        [SerializeField] private Color _penColor = Color.black;
        [SerializeField] private float _brushSize = 0.02f;
        [SerializeField] private float _brushSizeLine = 0.02f;
        private float _lastDrawnTime;
        [SerializeField] private float _timeInterval = 0.01f;

        [Header("Mode Toggles")]
        [Tooltip("Temporarily disable free draw entirely while focusing on line drawing.")]
        [SerializeField] private bool _freeDrawEnabled = true;

        [HideInInspector] public PenMode _currentMode;

        private Vector3 _pointAWorld;
        private Vector2 _pointAUV;
        private bool _pointASet = false;

        private WoodPaintSurface _currentWood;
        private IEventService _eventService;

        [Inject]
        private void Inject(IEventService eventService)
        {
            _eventService = eventService;
        }

        private void Start()
        {
            _currentMode = PenMode.FreeDraw;
        }

        private void Update()
        {
            if (Physics.Raycast(transform.position, -transform.forward,
                out RaycastHit hit, _rayDistance, _woodLayer))
            {
                WoodPaintSurface wood = hit.collider.GetComponent<WoodPaintSurface>();
                if (wood == null) return;

                _currentWood = wood;

                // Use the second UV channel (unique/non-overlapping unwrap),
                // NOT hit.textureCoord (UV0), which is the trim sheet's overlapping UVs.
                Vector2 uv = hit.textureCoord2;

                switch (_currentMode)
                {
                    case PenMode.FreeDraw:
                        if (!_freeDrawEnabled)
                            break;

                        if (Time.time - _lastDrawnTime >= _timeInterval)
                        {
                            _paintBrush.PaintAt(wood, uv, _penColor, _brushSize);
                            _eventService?.Publish(new PaintAppliedEvent(wood, hit.point, uv, _penColor, _brushSize, PenMode.FreeDraw));
                            _lastDrawnTime = Time.time;
                        }
                        break;

                    case PenMode.LineDrawing:
                        if (!_pointASet)
                        {
                            _pointAWorld = hit.point;
                            _pointAUV = uv;
                            _pointASet = true;
                            _eventService?.Publish(new PaintLineStartedEvent(_pointAWorld));
                        }
                        _lineDraw.UpdateLinePositions(_pointAWorld, hit.point);
                        break;
                }
            }
        }

        public void FinalizeLine()
        {
            if (!_pointASet || _currentWood == null)
            {
                ResetLineState();
                return;
            }

            if (!Physics.Raycast(transform.position, -transform.forward,
                out RaycastHit hit, _rayDistance, _woodLayer))
            {
                ResetLineState();
                return;
            }

            Vector2 finalUV = hit.textureCoord2;
            Vector3 finalWorldPoint = hit.point;

            float distance = Vector2.Distance(_pointAUV, finalUV);
            float brushSpacing = _brushSize * 0.5f;
            int steps = Mathf.Max(2, Mathf.CeilToInt(distance / brushSpacing));

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector2 stepUV = Vector2.Lerp(_pointAUV, finalUV, t);
                _paintBrush.PaintAt(_currentWood, stepUV, _penColor, _brushSizeLine);
            }

            _eventService?.Publish(new PaintLineCompletedEvent(_currentWood, _pointAWorld, finalWorldPoint));
            ResetLineState();
        }

        private void ResetLineState()
        {
            _pointASet = false;
            _currentMode = PenMode.FreeDraw;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, -transform.forward * _rayDistance);
        }
    }
}
