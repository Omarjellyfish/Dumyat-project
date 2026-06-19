using UnityEngine;

namespace DumyatRuntime
{
    public class PenTip : MonoBehaviour
    {
        [Header("Detection Settings")]
        [SerializeField] private LayerMask _woodLayer;
        [SerializeField] private float _rayDistance = 0.05f;
        [SerializeField] private PaintBrush _paintBrush;
        [SerializeField] private Color _penColor = Color.black;
        [SerializeField] private float _brushSize = 0.02f;

        private void Update()
        {
            if (Physics.Raycast(transform.position, -transform.forward,
                out RaycastHit hit, _rayDistance, _woodLayer))
            {
                WoodPaintSurface wood = hit.collider.GetComponent<WoodPaintSurface>();
                if (wood != null)
                    _paintBrush.PaintAt(wood, hit.textureCoord, _penColor, _brushSize);
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, -transform.forward * _rayDistance);
        }
    }
}