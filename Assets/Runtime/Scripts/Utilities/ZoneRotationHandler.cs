using DG.Tweening;
using UnityEngine;

namespace DumyatRuntime
{
    public class ZoneRotationHandler : MonoBehaviour
    {
        [Header("Target Objects")]
        [SerializeField] private Transform objectToRotate;

        [Header("Rotation Settings")]
        [Tooltip("Choose the local axis to rotate around")]
        [SerializeField] private Vector3 rotationAxis = Vector3.up; // e.g., (0, 1, 0) for Y axis
        
        [Tooltip("Use positive for clockwise, negative for counter-clockwise")]
        [SerializeField] private float speed = 100f;

        [Header("Collision Settings")]
        [SerializeField] private string targetTag = "MainCamera";

        private Tween rotationTween;

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag(targetTag))
            {
                StartRotating();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag(targetTag))
            {
                StopRotating();
            }
        }

        private void StartRotating()
        {
            // Safety check: Kill any existing tween before starting a new one
            if (rotationTween != null && rotationTween.IsActive())
            {
                rotationTween.Kill();
            }

            if (objectToRotate == null) return;

            // Calculate a 360-degree relative rotation vector based on axis and speed direction
            Vector3 targetRotation = rotationAxis.normalized * 360f * Mathf.Sign(speed);
            
            // Calculate duration based on absolute speed value (higher speed = shorter duration)
            float duration = 360f / Mathf.Abs(speed);

            // Create a continuous, linear local rotation loop
            rotationTween = objectToRotate.DOLocalRotate(targetRotation, duration, RotateMode.LocalAxisAdd)
                .SetEase(Ease.Linear)
                .SetLoops(-1, LoopType.Incremental);
        }

        private void StopRotating()
        {
            // Smoothly stop and clear the tween reference when exiting
            if (rotationTween != null && rotationTween.IsActive())
            {
                rotationTween.Kill();
            }
        }

        // Safety cleanup if the object is destroyed while tweening
        private void OnDestroy()
        {
            if (rotationTween != null)
            {
                rotationTween.Kill();
            }
        }
    }
}
