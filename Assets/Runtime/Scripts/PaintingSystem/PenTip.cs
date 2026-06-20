using UnityEngine;

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

        [HideInInspector] public PenMode _currentMode;

        private Vector3 _pointAWorld;
        private Vector2 _pointAUV;
        private bool _pointASet = false;

        private WoodPaintSurface _currentWood;

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

                switch (_currentMode)
                {
                    case PenMode.FreeDraw:
                        if (Time.time - _lastDrawnTime >= _timeInterval)
                        {
                            _paintBrush.PaintAt(wood, hit.textureCoord, _penColor, _brushSize);
                            _lastDrawnTime = Time.time;
                        }
                        break;

                    case PenMode.LineDrawing:
                        if (!_pointASet)
                        {
                            _pointAWorld = hit.point;
                            _pointAUV = hit.textureCoord;
                            _pointASet = true;
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

            Physics.Raycast(transform.position, -transform.forward,
                out RaycastHit hit, _rayDistance, _woodLayer);

            Vector2 finalUV = hit.textureCoord;

            float distance = Vector2.Distance(_pointAUV, finalUV);
            float brushSpacing = _brushSize * 0.5f;
            int steps = Mathf.Max(2, Mathf.CeilToInt(distance / brushSpacing));

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector2 stepUV = Vector2.Lerp(_pointAUV, finalUV, t);
                _paintBrush.PaintAt(_currentWood, stepUV, _penColor, _brushSizeLine);
            }

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