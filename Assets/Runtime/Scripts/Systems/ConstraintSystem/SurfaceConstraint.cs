using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    public abstract class SurfaceConstraint : MonoBehaviour
    {
        [SerializeField] private LayerMask _woodLayer;
        [SerializeField] protected GameObject _visualObject;
        [SerializeField] private float _detectionDistance = 0.1f;
        [SerializeField] private float _sideRayOffset = 0.02f;
        public bool IsConstrained => _isConstrained;
        [Header("Appearance Clearance")]
        [Tooltip("Adjust this value in the inspector if the tool still clips into the wood mesh.")]
        [SerializeField] private float _surfaceClearance = 0.005f; // NEW FIXED GAP VARIABLE

        protected Vector3 _hitPosition;
        protected Vector3 _hitNormal;
        protected Quaternion _lockedRotation;
        protected XRGrabInteractable _xrGrab;

        private Vector3 _handPositionAtSnap;
        protected Vector3 _automaticSnapOffset;
        private bool _isConstrained = false;

        // Captured once at Awake from however _visualObject is already positioned in the editor
        // relative to this transform. This is the offset that must be preserved every frame -
        // otherwise the mesh gets snapped back to sit exactly on the handle.
        protected Vector3 _visualLocalOffsetPosition;
        protected Quaternion _visualLocalOffsetRotation;

        protected virtual void Awake()
        {
            _xrGrab = GetComponent<XRGrabInteractable>();

            if (_visualObject != null)
            {
                _visualLocalOffsetPosition = transform.InverseTransformPoint(_visualObject.transform.position);
                _visualLocalOffsetRotation = Quaternion.Inverse(transform.rotation) * _visualObject.transform.rotation;
            }
        }

        private void OnEnable()
        {
            _xrGrab.activated.AddListener(OnActivate);
            _xrGrab.deactivated.AddListener(OnDeactivate);
        }

        private void OnDisable()
        {
            _xrGrab.activated.RemoveListener(OnActivate);
            _xrGrab.deactivated.RemoveListener(OnDeactivate);
        }

        private void LateUpdate()
        {
            if (_isConstrained)
            {
                ConstrainMovement();
            }
            else
            {
                // Keep the mesh's authored local offset (position+rotation relative to the saw
                // handle) instead of forcing it to exactly match the handle's world pose. Without
                // this, any offset given to _visualObject in the editor gets erased every frame.
                _visualObject.transform.position = transform.TransformPoint(_visualLocalOffsetPosition);
                _visualObject.transform.rotation = transform.rotation * _visualLocalOffsetRotation;
            }
        }

        private void OnActivate(ActivateEventArgs arg0)
        {
            if (TryDetectSurface(out RaycastHit hit, out Vector3 avgNormal))
            {
                _isConstrained = true;
                _hitPosition = hit.point;
                _hitNormal = avgNormal;
                _lockedRotation = transform.rotation;

                _handPositionAtSnap = GetTrackedHandPosition();

                // FIXED: Instead of tracking hand layout, push up out of the wood by our clearance value
                _automaticSnapOffset = avgNormal * _surfaceClearance;

                Debug.Log($"Automatically snapped flush to surface of: {hit.collider.name}");
            }
        }

        private void OnDeactivate(DeactivateEventArgs arg0)
        {
            _isConstrained = false;
        }

        protected Vector3 GetTrackedHandPosition()
        {
            if (_xrGrab != null && _xrGrab.isSelected && _xrGrab.interactorsSelecting.Count > 0)
            {
                return _xrGrab.interactorsSelecting[0].transform.position;
            }
            return transform.position;
        }

        protected Vector3 GetMovementSinceSnap()
        {
            return GetTrackedHandPosition() - _handPositionAtSnap;
        }

        private bool TryDetectSurface(out RaycastHit hit, out Vector3 averagedNormal)
        {
            Vector3 origin = transform.position;
            Vector3 dir = -transform.up;
            Vector3 side = transform.right * _sideRayOffset;

            bool cHit = Physics.Raycast(origin, dir, out RaycastHit hC, _detectionDistance, _woodLayer);
            bool lHit = Physics.Raycast(origin - side, dir, out RaycastHit hL, _detectionDistance, _woodLayer);
            bool rHit = Physics.Raycast(origin + side, dir, out RaycastHit hR, _detectionDistance, _woodLayer);

            if (!cHit && !lHit && !rHit)
            {
                hit = default;
                averagedNormal = Vector3.up;
                return false;
            }

            hit = cHit ? hC : (lHit ? hL : hR);

            Vector3 normalSum = Vector3.zero;
            int count = 0;
            if (cHit) { normalSum += hC.normal; count++; }
            if (lHit) { normalSum += hL.normal; count++; }
            if (rHit) { normalSum += hR.normal; count++; }
            averagedNormal = (normalSum / count).normalized;

            return true;
        }

        protected abstract void ConstrainMovement();

        private void OnDrawGizmos()
        {
            Vector3 origin = transform.position;
            Vector3 dir = -transform.up;
            Vector3 side = transform.right * _sideRayOffset;

            DrawRayGizmo(origin, dir, Color.yellow);
            DrawRayGizmo(origin - side, dir, Color.cyan);
            DrawRayGizmo(origin + side, dir, Color.cyan);
        }

        private void DrawRayGizmo(Vector3 origin, Vector3 dir, Color color)
        {
            Gizmos.color = color;
            Gizmos.DrawSphere(origin, 0.003f);

            if (Physics.Raycast(origin, dir, out RaycastHit hit, _detectionDistance, _woodLayer))
            {
                Gizmos.DrawLine(origin, hit.point);
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(hit.point, 0.004f);
            }
            else
            {
                Gizmos.DrawLine(origin, origin + dir * _detectionDistance);
            }
        }
    }
}