using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    public class CrowbarTool : MonoBehaviour
    {
        [Header("Settings")]
        // Reusing the hammer settings so you don't have to create a new ScriptableObject
        // You can duplicate the Hammer's SO and tweak the hit velocities/sounds for the crowbar.
        [SerializeField] private NailHammerSettingsSO _settings;

        private XRGrabInteractable _grabInteractable;
        private Rigidbody _rb;

        private Vector3 _lastPosition;
        private Vector3 _currentVelocity;
        private bool _isGrabbed;
        private float _lastImpactTime = -999f;

        public event Action<Vector3, GameObject, GameObject> OnAttachmentRemoved;

        #region Lifecycle
        private void Awake()
        {
            _grabInteractable = GetComponent<XRGrabInteractable>();
            _rb = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            _grabInteractable.selectEntered.AddListener(HandleSelectEntered);
            _grabInteractable.selectExited.AddListener(HandleSelectExited);
        }

        private void OnDisable()
        {
            _grabInteractable.selectEntered.RemoveListener(HandleSelectEntered);
            _grabInteractable.selectExited.RemoveListener(HandleSelectExited);
        }

        private void Update()
        {
            if (!_isGrabbed) return;

            Vector3 currentPosition = transform.position;
            _currentVelocity = (currentPosition - _lastPosition) / Time.deltaTime;
            _lastPosition = currentPosition;
        }
        #endregion

        #region XRI Grab Callbacks
        private void HandleSelectEntered(SelectEnterEventArgs args)
        {
            _isGrabbed = true;
            _lastPosition = transform.position;
            _currentVelocity = Vector3.zero;
        }

        private void HandleSelectExited(SelectExitEventArgs args)
        {
            _isGrabbed = false;
            _currentVelocity = Vector3.zero;
        }
        #endregion

        #region Collision Pipeline
        private void OnCollisionEnter(Collision collision)
        {
            if (!_isGrabbed) return;
            if (Time.time < _lastImpactTime + _settings.impactCooldown) return;

            float speed = _currentVelocity.magnitude;
            if (speed < _settings.minImpactVelocity) return;

            // Optional: Trigger a light haptic tap just for hitting something hard enough
            HapticFeedback.Singleton?.TriggerVibration(
                _settings.generalImpactHapticProfile, XRInteractionUtils.GetGrabbingHand(_grabInteractable));

            Rigidbody plankABody = collision.rigidbody;
            if (plankABody == null) return;
            if (plankABody.GetComponent<StructuralPiece>() == null) return;

            ContactPoint contact = collision.GetContact(0);

            // Look for what this piece might be attached to behind the impact point
            Rigidbody plankBBody = FindSecondBody(contact, collision.collider);
            if (plankBBody == null || plankABody == plankBBody) return;

            _lastImpactTime = Time.time;

            // Check if they are actually bonded
            FixedJoint existingJoint = FindExistingJoint(plankABody, plankBBody);

            if (existingJoint != null)
            {
                RemoveAttachment(plankABody, plankBBody, existingJoint, contact);
            }
        }
        #endregion

        #region Second Body Discovery
        private Rigidbody FindSecondBody(ContactPoint contact, Collider plankACollider)
        {
            Vector3 rayDirection = _currentVelocity.normalized;
            float rayDistance = _settings.raycastDistance;
            Vector3 rayOrigin = contact.point + rayDirection * 0.01f;

            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, rayDirection, rayDistance);
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == plankACollider) continue;

                Rigidbody hitBody = hit.collider.attachedRigidbody;
                if (hitBody != null && hitBody.GetComponent<StructuralPiece>() != null)
                    return hitBody;
            }
            return null;
        }
        #endregion

        #region Joint Management
        private FixedJoint FindExistingJoint(Rigidbody bodyA, Rigidbody bodyB)
        {
            foreach (FixedJoint joint in bodyA.GetComponents<FixedJoint>())
            {
                if (joint.connectedBody == bodyB) return joint;
            }
            foreach (FixedJoint joint in bodyB.GetComponents<FixedJoint>())
            {
                if (joint.connectedBody == bodyA) return joint;
            }
            return null;
        }

        private void RemoveAttachment(Rigidbody bodyA, Rigidbody bodyB, FixedJoint joint, ContactPoint contact)
        {
            // 1. Break the physics bond
            Destroy(joint);

            // 2. Restore collisions if no other joints hold these two specific pieces together
            int remainingJointCount = CountJointsBetween(bodyA, bodyB, joint);
            if (remainingJointCount == 0)
            {
                RestoreCollisionsBetween(bodyA.gameObject, bodyB.gameObject);
            }

            // 3. Clean up the visual (Nail or Glue)
            DestroyClosestNailVisual(bodyA, bodyB, contact.point);

            // 4. Feedback & Events
            SoundManager.Singleton?.PlayRandomSoundFxAtPosition(
                _settings.nailOutClips, transform, _settings.sfxVolume);

            HapticFeedback.Singleton?.TriggerVibration(
                _settings.nailOutHapticProfile, XRInteractionUtils.GetGrabbingHand(_grabInteractable));

            Debug.Log($"[CrowbarTool] Detached '{bodyA.name}' ↔ '{bodyB.name}' at {contact.point}");
            OnAttachmentRemoved?.Invoke(contact.point, bodyA.gameObject, bodyB.gameObject);
        }

        private int CountJointsBetween(Rigidbody bodyA, Rigidbody bodyB, FixedJoint excludeJoint)
        {
            int count = 0;
            foreach (FixedJoint joint in bodyA.GetComponents<FixedJoint>())
            {
                if (joint == excludeJoint) continue;
                if (joint.connectedBody == bodyB) count++;
            }
            foreach (FixedJoint joint in bodyB.GetComponents<FixedJoint>())
            {
                if (joint == excludeJoint) continue;
                if (joint.connectedBody == bodyA) count++;
            }
            return count;
        }
        #endregion

        #region Collision Restoring
        private void RestoreCollisionsBetween(GameObject a, GameObject b)
        {
            Collider[] collidersA = a.GetComponentsInChildren<Collider>();
            Collider[] collidersB = b.GetComponentsInChildren<Collider>();
            foreach (Collider colA in collidersA)
            {
                foreach (Collider colB in collidersB)
                {
                    Physics.IgnoreCollision(colA, colB, false);
                }
            }
        }
        #endregion

        #region Visual Cleanup
        private void DestroyClosestNailVisual(Rigidbody bodyA, Rigidbody bodyB, Vector3 worldPoint)
        {
            NailVisualMarker closestMarker = null;
            float closestDistance = float.MaxValue;

            SearchMarkersOnBody(bodyA, bodyB, worldPoint, ref closestMarker, ref closestDistance);
            SearchMarkersOnBody(bodyB, bodyA, worldPoint, ref closestMarker, ref closestDistance);

            if (closestMarker != null)
            {
                Destroy(closestMarker.gameObject);
            }
        }

        private void SearchMarkersOnBody(Rigidbody searchBody, Rigidbody otherBody, Vector3 worldPoint,
                                         ref NailVisualMarker closestMarker, ref float closestDistance)
        {
            NailVisualMarker[] markers = searchBody.GetComponentsInChildren<NailVisualMarker>();
            foreach (NailVisualMarker marker in markers)
            {
                bool matchesForward = marker.ConnectedBodyA == searchBody && marker.ConnectedBodyB == otherBody;
                bool matchesReverse = marker.ConnectedBodyA == otherBody && marker.ConnectedBodyB == searchBody;

                if (!matchesForward && !matchesReverse) continue;

                float distance = Vector3.Distance(marker.transform.position, worldPoint);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestMarker = marker;
                }
            }
        }
        #endregion
    }
}