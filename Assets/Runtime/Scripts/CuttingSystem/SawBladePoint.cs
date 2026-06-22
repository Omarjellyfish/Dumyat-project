using UnityEngine;

namespace DumyatRuntime
{
    public class SawBladePoint : MonoBehaviour
    {
        [SerializeField] private LayerMask _woodLayer;
        [SerializeField] private float _rayDistance = 0.05f;
        public bool _isTouchingWood = false;

        public Vector3 _hitPoint;
        public Vector3 _hitNormal;
        public GameObject _hitObject;
        public void CheckContact()
        {
            if (Physics.Raycast(transform.position, -transform.forward, out RaycastHit hit, _rayDistance, _woodLayer))
            {
                _hitNormal = hit.normal; 
                _hitPoint = hit.point;
                _isTouchingWood = true;
                _hitObject = hit.collider.gameObject;
            }
            else
                _isTouchingWood = false;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = _isTouchingWood ? Color.green : Color.red;
            Gizmos.DrawRay(transform.position, -transform.forward * _rayDistance);
        }
    }
}
