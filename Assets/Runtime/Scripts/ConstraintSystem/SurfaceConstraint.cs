using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
namespace DumyatRuntime
{
    public abstract class SurfaceConstraint : MonoBehaviour
    {
        [SerializeField] LayerMask _woodLayer;
        Vector3 _hitPosition;
        Vector3 _hitNormal;
        Quaternion _lockRotation;
        XRGrabInteractable _xrGrab;
        GameObject _visualObject;
        [SerializeField] Vector3 offset;
        [SerializeField] private Vector3 _boxHalfExtents = new Vector3(0.02f, 0.02f, 0.02f);
        [SerializeField] private float _detectionDistance = 0.1f;
        bool _isConstrained = false;
        RaycastHit hit;
        protected virtual void Awake()
        {
            _xrGrab = GetComponent<XRGrabInteractable>();
        }

        void OnEnable()
        {
            _xrGrab.activated.AddListener(OnActivate);
            _xrGrab.deactivated.AddListener(OnDeactivate);
        }
        private void OnActivate(ActivateEventArgs arg0)
        {
            if (Physics.BoxCast(
                transform.position + offset,
                _boxHalfExtents,
                transform.forward,
                out hit,
                transform.rotation,
                _detectionDistance,
                _woodLayer))
            {
                _isConstrained = true;
                _hitPosition = hit.point;
                _hitNormal = hit.normal;
                _lockRotation = transform.rotation;
            }
        }

        void OnDisable()
        {
            _xrGrab.activated.RemoveListener(OnActivate);
            _xrGrab.deactivated.RemoveListener(OnDeactivate);
        }
        private void OnDeactivate(DeactivateEventArgs arg0)
        {
            _isConstrained = false;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Matrix4x4 rotationMatrix = Matrix4x4.TRS(transform.position + offset, transform.rotation, Vector3.one);
            Gizmos.matrix = rotationMatrix;
            Gizmos.DrawWireCube(Vector3.zero, _boxHalfExtents * 2f);
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawRay(transform.position + offset, transform.forward * _detectionDistance);
        }
    }
}
