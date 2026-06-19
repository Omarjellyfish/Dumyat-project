using UnityEngine;

namespace DumyatRuntime
{
    public class PenTip : MonoBehaviour
    {
        [Header("Detection Settings")]
        [SerializeField] private LayerMask _woodLayer;
        [SerializeField] private float _drawInterval = 0.05f;
        [SerializeField] private float _rayDistance = 0.05f;
        [SerializeField] private DecalPainter _decalPainter;
        Vector3 _lastDrawPosition;
        private float _lastDrawTime;

        private void Update()
        {
            if (Time.time - _lastDrawTime < _drawInterval) return;
            if (Physics.Raycast(transform.position, -transform.forward, out RaycastHit hit, _rayDistance, _woodLayer))
            {
                if (Vector3.Distance(_lastDrawPosition, hit.point) < 0.005f)
                    return;
                _lastDrawPosition = hit.point;
                _lastDrawTime = Time.time;
                _decalPainter.SpawnDecal(hit.point, hit.normal);
            }
        }
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, -transform.forward * _rayDistance);
        }
    }
}