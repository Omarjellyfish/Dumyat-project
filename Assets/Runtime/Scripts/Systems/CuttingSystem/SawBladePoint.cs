using UnityEngine;

namespace DumyatRuntime
{
    public class SawBladePoint : MonoBehaviour
    {
        [SerializeField] private LayerMask _woodLayer;
        [SerializeField] private float _rayDistance = 0.05f;
        [SerializeField] private bool _showDebugRayInGame = true;
        [SerializeField] private float _debugRayWidth = 0.005f;

        private LineRenderer _debugLine;

        [HideInInspector] public bool _isTouchingWood = false;

        [HideInInspector] public Vector3 _hitPoint;
        [HideInInspector] public Vector3 _hitNormal;
        [HideInInspector] public GameObject _hitObject;

        private void Awake()
        {
            SetupDebugLine();
        }

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

            UpdateDebugLine();
        }

        private void SetupDebugLine()
        {
            if (!_showDebugRayInGame)
                return;

            _debugLine = GetComponent<LineRenderer>();
            if (_debugLine == null)
                _debugLine = gameObject.AddComponent<LineRenderer>();

            _debugLine.positionCount = 2;
            _debugLine.startWidth = _debugRayWidth;
            _debugLine.endWidth = _debugRayWidth;
            _debugLine.useWorldSpace = true;
            _debugLine.material = new Material(Shader.Find("Sprites/Default"));
        }

        private void UpdateDebugLine()
        {
            if (!_showDebugRayInGame || _debugLine == null)
                return;

            Color rayColor = _isTouchingWood ? Color.green : Color.red;
            _debugLine.startColor = rayColor;
            _debugLine.endColor = rayColor;
            _debugLine.SetPosition(0, transform.position);
            _debugLine.SetPosition(1, transform.position + -transform.forward * _rayDistance);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = _isTouchingWood ? Color.green : Color.red;
            Gizmos.DrawRay(transform.position, -transform.forward * _rayDistance);
        }
    }
}
