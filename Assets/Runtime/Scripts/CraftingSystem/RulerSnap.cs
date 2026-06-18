using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    public class RulerSnap : MonoBehaviour
    {
        private XRGrabInteractable _grabInteractable;
        [SerializeField] float DetectionRadius;
        [SerializeField] float ThresholdDistance;
        float CurrentDistance;
        Vector3 currentPoint;
        [SerializeField] LayerMask woodLayer;
        Rigidbody rig;
        void Awake()
        {
            _grabInteractable = GetComponent<XRGrabInteractable>();
            rig = GetComponent<Rigidbody>();
        }

        void OnEnable()
        {
            _grabInteractable.selectEntered.AddListener(OnGrabbed);
            _grabInteractable.selectExited.AddListener(OnReleased);
        }

        void OnDisable()
        {
            _grabInteractable.selectEntered.RemoveListener(OnGrabbed);
            _grabInteractable.selectExited.RemoveListener(OnReleased);
        }

        void Update()
        {
            RaycastHit hit;
            if (Physics.SphereCast(transform.position, DetectionRadius, transform.forward, out hit, ThresholdDistance, woodLayer))
            {
                CurrentDistance = hit.distance;
                currentPoint = hit.point;
            }
            else
            {
                CurrentDistance = Mathf.Infinity;
            }
        }

        void OnGrabbed(SelectEnterEventArgs _args)
        {
            rig.isKinematic = false;
        }

        void OnReleased(SelectExitEventArgs _args)
        {
            if (CurrentDistance < ThresholdDistance)
            {
                transform.position = currentPoint;
                rig.isKinematic = true;
            }
        }
    }
}
