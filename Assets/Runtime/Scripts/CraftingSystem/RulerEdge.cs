using UnityEngine;

namespace DumyatRuntime
{
    public class RulerEdge : MonoBehaviour
    {
        [SerializeField] private Transform _edgeStart;
        [SerializeField] private Transform _edgeEnd;

        public Vector3 GetClosestPointOnEdge(Vector3 worldPoint)
        {
            Vector3 edgeDirection = (_edgeEnd.position - _edgeStart.position).normalized;
            float edgeLength = Vector3.Distance(_edgeStart.position, _edgeEnd.position);
            float projectedDistance = Vector3.Dot(worldPoint - _edgeStart.position, edgeDirection);
            float clampedDistance = Mathf.Clamp(projectedDistance, 0f, edgeLength);
            return _edgeStart.position + edgeDirection * clampedDistance;
        }

        private void OnDrawGizmos()
        {
            if (_edgeStart == null || _edgeEnd == null) return;
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(_edgeStart.position, _edgeEnd.position);
            Gizmos.DrawSphere(_edgeStart.position, 0.01f);
            Gizmos.DrawSphere(_edgeEnd.position, 0.01f);
        }
    }
}