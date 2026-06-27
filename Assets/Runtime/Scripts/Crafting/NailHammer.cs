using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace DumyatRuntime
{
    /// <summary>
    /// Physics-based VR hammer that attaches or detaches overlapping StructuralPiece
    /// objects using FixedJoints. Spawns cosmetic nail visuals at the contact point.
    ///
    /// Attach to the same GameObject as the XRGrabInteractable.
    /// Child collider events (e.g. the hammer head) bubble up to this Rigidbody.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    public class NailHammer : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private NailHammerSettingsSO _settings;

        // --- Cached Components ---
        private XRGrabInteractable _grabInteractable;
        private Rigidbody _rb;

        // --- Velocity Tracking ---
        private Vector3 _lastPosition;
        private Vector3 _currentVelocity;
        private bool _isGrabbed;

        // --- Cooldown ---
        private float _lastImpactTime = -999f;

        // --- Events ---
        /// <summary>Fired after a new FixedJoint + nail visual is created.</summary>
        public event Action<Vector3, GameObject, GameObject> OnNailAttached;

        /// <summary>Fired after an existing FixedJoint + nail visual is removed.</summary>
        public event Action<Vector3, GameObject, GameObject> OnNailRemoved;

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

        /// <summary>
        /// Manual velocity calculation. XRI sets held Rigidbodies to kinematic,
        /// making Rigidbody.velocity unreliable — so we track it ourselves.
        /// </summary>
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
            // Gate 1: Must be grabbed — prevents physics-only drops from triggering
            if (!_isGrabbed) return;

            // Gate 2: Cooldown — prevents multi-joint spawns from a single bounce
            if (Time.time < _lastImpactTime + _settings.impactCooldown) return;

            // Gate 3: Velocity threshold
            float speed = _currentVelocity.magnitude;
            if (speed < _settings.minImpactVelocity) return;

            // Gate 4: Struck object must be a StructuralPiece
            Rigidbody plankABody = collision.rigidbody;
            if (plankABody == null) return;
            if (plankABody.GetComponent<StructuralPiece>() == null) return;

            // Get the primary contact point
            ContactPoint contact = collision.GetContact(0);

            // Gate 5: Find the second body behind the struck one
            Rigidbody plankBBody = FindSecondBody(contact, collision.collider);
            if (plankBBody == null) return;

            // Gate 6: Can't attach an object to itself
            if (plankABody == plankBBody) return;

            // --- All gates passed. Commit the cooldown. ---
            _lastImpactTime = Time.time;

            // Toggle: if a joint already exists between A↔B, remove it. Otherwise, create one.
            FixedJoint existingJoint = FindExistingJoint(plankABody, plankBBody);

            if (existingJoint != null)
            {
                RemoveAttachment(plankABody, plankBBody, existingJoint, contact);
            }
            else
            {
                CreateAttachment(plankABody, plankBBody, contact);
            }
        }

        #endregion

        #region Second Body Discovery

        /// <summary>
        /// Raycasts from the contact point along the hammer's velocity vector to find
        /// the StructuralPiece behind the struck plank.
        /// </summary>
        private Rigidbody FindSecondBody(ContactPoint contact, Collider plankACollider)
        {
            Vector3 rayDirection = _currentVelocity.normalized;
            float rayDistance = _settings.raycastDistance;

            // Nudge the origin slightly past the contact surface to avoid self-hits
            Vector3 rayOrigin = contact.point + rayDirection * 0.01f;

            // Visualize the raycast for debugging (visible for 2 seconds)
            Debug.DrawRay(rayOrigin, rayDirection * rayDistance, Color.red, 2f);

            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, rayDirection, rayDistance);

            foreach (RaycastHit hit in hits)
            {
                // Skip the struck plank's own collider
                if (hit.collider == plankACollider) continue;

                Rigidbody hitBody = hit.collider.attachedRigidbody;
                if (hitBody != null && hitBody.GetComponent<StructuralPiece>() != null)
                    return hitBody;
            }

            return null;
        }

        #endregion

        #region Joint Management

        /// <summary>
        /// Searches for an existing FixedJoint between bodyA and bodyB (checks both directions).
        /// Returns the first match, or null.
        /// </summary>
        private FixedJoint FindExistingJoint(Rigidbody bodyA, Rigidbody bodyB)
        {
            // Check joints on A connecting to B
            foreach (FixedJoint joint in bodyA.GetComponents<FixedJoint>())
            {
                if (joint.connectedBody == bodyB) return joint;
            }

            // Check joints on B connecting to A
            foreach (FixedJoint joint in bodyB.GetComponents<FixedJoint>())
            {
                if (joint.connectedBody == bodyA) return joint;
            }

            return null;
        }

        private void CreateAttachment(Rigidbody plankA, Rigidbody plankB, ContactPoint contact)
        {
            // --- FixedJoint ---
            FixedJoint joint = plankA.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = plankB;
            joint.anchor = plankA.transform.InverseTransformPoint(contact.point);
            joint.breakForce = _settings.jointBreakForce;
            joint.breakTorque = _settings.jointBreakTorque;

            // Prevent the physics solver from pushing the joined pieces apart
            IgnoreCollisionsBetween(plankA.gameObject, plankB.gameObject);

            // --- Nail Visual ---
            SpawnNailVisual(contact, plankA.transform, plankA, plankB);

            // --- Audio ---
            SoundManager.Singleton?.PlayRandomSoundFxAtPosition(
                _settings.nailInClips, transform, _settings.sfxVolume);

            // --- Haptics ---
            HapticFeedback.Singleton?.TriggerVibration(
                _settings.nailInHapticProfile, GetGrabbingHand());

            Debug.Log($"[NailHammer] Attached '{plankA.name}' ↔ '{plankB.name}' at {contact.point}");
            OnNailAttached?.Invoke(contact.point, plankA.gameObject, plankB.gameObject);
        }

        private void RemoveAttachment(Rigidbody bodyA, Rigidbody bodyB,
                                       FixedJoint joint, ContactPoint contact)
        {
            Destroy(joint);

            // Only re-enable collisions if NO other joints remain between A↔B
            // (must check after Destroy — Destroy is deferred to end of frame,
            //  so we check via FindExistingJoint which will still see the dying joint.
            //  Instead, we count manually.)
            int remainingJointCount = CountJointsBetween(bodyA, bodyB, joint);
            if (remainingJointCount == 0)
            {
                RestoreCollisionsBetween(bodyA.gameObject, bodyB.gameObject);
            }

            // --- Nail Visual ---
            DestroyClosestNailVisual(bodyA, bodyB, contact.point);

            // --- Audio ---
            SoundManager.Singleton?.PlayRandomSoundFxAtPosition(
                _settings.nailOutClips, transform, _settings.sfxVolume);

            // --- Haptics ---
            HapticFeedback.Singleton?.TriggerVibration(
                _settings.nailOutHapticProfile, GetGrabbingHand());

            Debug.Log($"[NailHammer] Detached '{bodyA.name}' ↔ '{bodyB.name}' at {contact.point}");
            OnNailRemoved?.Invoke(contact.point, bodyA.gameObject, bodyB.gameObject);
        }

        /// <summary>
        /// Counts FixedJoints between bodyA and bodyB, excluding the one being destroyed.
        /// </summary>
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

        #region Collision Ignoring

        private void IgnoreCollisionsBetween(GameObject a, GameObject b)
        {
            Collider[] collidersA = a.GetComponentsInChildren<Collider>();
            Collider[] collidersB = b.GetComponentsInChildren<Collider>();

            foreach (Collider colA in collidersA)
            {
                foreach (Collider colB in collidersB)
                {
                    Physics.IgnoreCollision(colA, colB, true);
                }
            }
        }

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

        #region Nail Visuals

        private void SpawnNailVisual(ContactPoint contact, Transform parent,
                                      Rigidbody bodyA, Rigidbody bodyB)
        {
            if (_settings.nailPrefab == null)
            {
                Debug.Log($"[NailHammer] Nail prefab not assigned — skipping visual spawn at {contact.point}");
                return;
            }

            Quaternion nailRotation = Quaternion.LookRotation(contact.normal);
            GameObject nail = Instantiate(_settings.nailPrefab, contact.point, nailRotation, parent);

            // Tag with marker for tracking during removal
            NailVisualMarker marker = nail.AddComponent<NailVisualMarker>();
            marker.ConnectedBodyA = bodyA;
            marker.ConnectedBodyB = bodyB;
        }

        /// <summary>
        /// Finds the NailVisualMarker on the parent plank that matches the A↔B pair
        /// and is closest to the given world position, then destroys it.
        /// </summary>
        private void DestroyClosestNailVisual(Rigidbody bodyA, Rigidbody bodyB, Vector3 worldPoint)
        {
            NailVisualMarker closestMarker = null;
            float closestDistance = float.MaxValue;

            // Search markers on both planks (nail is parented to plankA, but check both)
            SearchMarkersOnBody(bodyA, bodyB, worldPoint, ref closestMarker, ref closestDistance);
            SearchMarkersOnBody(bodyB, bodyA, worldPoint, ref closestMarker, ref closestDistance);

            if (closestMarker != null)
            {
                Destroy(closestMarker.gameObject);
            }
        }

        private void SearchMarkersOnBody(Rigidbody searchBody, Rigidbody otherBody,
                                          Vector3 worldPoint,
                                          ref NailVisualMarker closestMarker,
                                          ref float closestDistance)
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

        #region Haptic Hand Detection

        private HapticHand GetGrabbingHand()
        {
            if (_grabInteractable.interactorsSelecting.Count == 0)
                return HapticHand.Right; // fallback

            IXRSelectInteractor interactor = _grabInteractable.interactorsSelecting[0];
            string interactorName = (interactor as MonoBehaviour)?.gameObject.name.ToLower() ?? "";

            return interactorName.Contains("left") ? HapticHand.Left : HapticHand.Right;
        }

        #endregion
    }

    /// <summary>
    /// Lightweight marker component added to spawned nail visuals at runtime.
    /// Stores which two Rigidbodies the nail connects, enabling targeted cleanup on removal.
    /// </summary>
    public class NailVisualMarker : MonoBehaviour
    {
        [HideInInspector] public Rigidbody ConnectedBodyA;
        [HideInInspector] public Rigidbody ConnectedBodyB;
    }
}
