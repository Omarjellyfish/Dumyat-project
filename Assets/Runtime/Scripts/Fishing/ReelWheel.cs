using System;
using System.Security.Claims;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    public class ReelWheel : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The center pivot point around which the wheel rolls.")]
        [SerializeField] private Transform _rotationPivot;

        [Tooltip("The visual 3D model of the reel/wheel that will visually spin.")]
        [SerializeField] private Transform _visualTransform;

        [Tooltip("The XR Simple Interactable attached directly to the handle collider that the player grabs.")]
        [SerializeField] private XRSimpleInteractable _handleInteractable;

        [Header("Settings")]
        [Tooltip("The local axis of the visual transform around which the reel rolls.")]
        [SerializeField] private Vector3 _localRotationAxis = Vector3.right;

        [Tooltip("The maximum rotation degrees allowed per frame to prevent wild jumping.")]
        [SerializeField] private float _maxRotationPerFrame = 15f;

        // Logic State
        public float CurrentRotation { get; private set; } = 0f;
        public float CurrentRotationExposed = 0;
        public static ReelWheel instance;
        private Transform _handOnReel;
        private Vector3 _previousHandDirection;

        private void OnEnable()
        {
            if (_handleInteractable != null)
            {
                _handleInteractable.selectEntered.AddListener(OnGrabbed);
                _handleInteractable.selectExited.AddListener(OnReleased);
                CurrentRotationExposed = CurrentRotation;
            }
            else
            {
                Debug.LogWarning("ReelWheel is missing a reference to the Handle's XRSimpleInteractable. Interaction won't work.", this);
            }
        }

        private void OnDisable()
        {
            if (_handleInteractable != null)
            {
                _handleInteractable.selectEntered.RemoveListener(OnGrabbed);
                _handleInteractable.selectExited.RemoveListener(OnReleased);
            }
        }

        private void Update()
        {
            HandleRotation();
            CurrentRotationExposed = CurrentRotation;
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            if (args.interactorObject != null)
            {
                _handOnReel = args.interactorObject.transform;
                _previousHandDirection = GetDirectionFromPivot(_handOnReel.position);
            }
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            _handOnReel = null;
        }

        private void HandleRotation()
        {
            if (_handOnReel != null)
            {
                float addedRotation = CalculateAddedRotation(_handOnReel);

                // Clamp to not exceed the max rotation per frame limiting erratic behavior
                addedRotation = Mathf.Clamp(addedRotation, -_maxRotationPerFrame, _maxRotationPerFrame);

                // Update the state and visual transform natively
                if (Mathf.Abs(addedRotation) > 0.001f)
                {
                    CurrentRotation += addedRotation;

                    if (_visualTransform != null)
                    {
                        _visualTransform.localRotation = Quaternion.AngleAxis(CurrentRotation, _localRotationAxis);
                    }
                }
            }
        }

        private float CalculateAddedRotation(Transform hand)
        {
            Vector3 currentHandDirection = GetDirectionFromPivot(hand.position);
            Transform pivotOrSelf = _rotationPivot != null ? _rotationPivot : transform;

            Vector3 worldRotationAxis = pivotOrSelf.TransformDirection(_localRotationAxis);

            // Calculate signed angle between the previous hand direction and the current one around the rotation pivot axis
            float angleDelta = Vector3.SignedAngle(_previousHandDirection, currentHandDirection, worldRotationAxis);

            _previousHandDirection = currentHandDirection;

            return angleDelta;
        }

        private Vector3 GetDirectionFromPivot(Vector3 targetPosition)
        {
            Transform pivotOrSelf = _rotationPivot != null ? _rotationPivot : transform;
            // Vector pointing from pivot to target (e.g. hand)
            return (targetPosition - pivotOrSelf.position).normalized;
        }
    }
}