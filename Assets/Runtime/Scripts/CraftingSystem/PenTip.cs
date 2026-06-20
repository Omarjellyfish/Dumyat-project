using UnityEngine;

namespace DumyatRuntime
{

    public class PenTip : MonoBehaviour
    {
        [Header("Detection Settings")]
        [SerializeField] private LayerMask _woodLayer;
        [SerializeField] private float _rayDistance = 0.05f;
        [SerializeField] private PaintBrush _paintBrush;
        [SerializeField] LineDrawing _linedraw;
        [SerializeField] private Color _penColor = Color.black;
        [SerializeField] private float _brushSize = 0.02f;
        float _lastDrawnTime;
        [SerializeField] float _timeIntervel = 0.01f;
        [HideInInspector] public PenMode _currentMode;
        private Vector3 _pointA;
        private bool _pointASet = false;

        void Start()
        {
            _currentMode = PenMode.FreeDraw;
        }


        private void Update()
        {
            if (Physics.Raycast(transform.position, -transform.forward,
                out RaycastHit hit, _rayDistance, _woodLayer))
            {
                WoodPaintSurface wood = hit.collider.GetComponent<WoodPaintSurface>();
                if (wood != null)
                {
                    switch (_currentMode)
                    {
                        case PenMode.FreeDraw:
                            if (Time.time - _lastDrawnTime >= _timeIntervel)
                            {
                                _paintBrush.PaintAt(wood, hit.textureCoord, _penColor, _brushSize);
                                _lastDrawnTime = Time.time;
                            }
                            break;

                        case PenMode.LineDrawing:
                            if (!_pointASet)
                            {
                                _pointA = hit.point;
                                _pointASet = true;
                            }
                            _linedraw.UpdateLinePositions(_pointA, hit.point);
                            break;
                    }
                }
            }
        }
        public void ExitLineDrawingMode()
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