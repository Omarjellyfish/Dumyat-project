using UnityEngine;

namespace DumyatRuntime
{
    public enum PenMode
    {
        FreeDraw,
        LineDrawing,
        Spray
    }
    public class PenTip : MonoBehaviour
    {
        [Header("Detection Settings")]
        [SerializeField] private LayerMask _woodLayer;
        [SerializeField] private float _rayDistance = 0.05f;
        [SerializeField] private PaintBrush _paintBrush;
        [SerializeField] private Color _penColor = Color.black;
        [SerializeField] private float _brushSize = 0.02f;
        float _lastDrawnTime;
        [SerializeField] float _timeIntervel = 0.01f;
        private PenMode _currentMode ;

        void Start()
        {
            _currentMode = PenMode.FreeDraw;
        }
        private void Update()
        {

            switch (_currentMode)
            {
                case PenMode.FreeDraw:
                    if (Physics.Raycast(transform.position, -transform.forward,
                        out RaycastHit hit, _rayDistance, _woodLayer))
                    {
                        WoodPaintSurface wood = hit.collider.GetComponent<WoodPaintSurface>();
                        if (wood != null)
                        {
                            if (Time.time - _lastDrawnTime >= _timeIntervel)
                            {
                                _paintBrush.PaintAt(wood, hit.textureCoord, _penColor, _brushSize);
                                _lastDrawnTime = Time.time;
                            }
                        }
                    }
                    break;
                case PenMode.LineDrawing:
                    break;
                case PenMode.Spray:
                    break;
            }
        }
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, -transform.forward * _rayDistance);
        }
    }
}