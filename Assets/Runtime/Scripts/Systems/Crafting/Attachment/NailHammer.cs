using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Reflex.Attributes;
namespace DumyatRuntime
{
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    public class NailHammer : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private NailHammerSettingsSO _settings;

        private ISoundService _soundService;
        private IHapticService _hapticsService;
        private IEventService _eventService;




        private XRGrabInteractable _grabInteractable;
        private Rigidbody _rb;
        private Vector3 _lastPosition;
        private Vector3 _currentVelocity;
        private bool _isGrabbed;
        private float _lastImpactTime = -999f;
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

            _hapticsService?.TriggerVibration(
                _settings.generalImpactHapticProfile, XRInteractionUtils.GetGrabbingHand(_grabInteractable));

            Rigidbody plankABody = collision.rigidbody;
            if (plankABody == null) return;
            if (plankABody.GetComponent<StructuralPiece>() == null) return;
            ContactPoint contact = collision.GetContact(0);
            Rigidbody plankBBody = FindSecondBody(contact, collision.collider);
            if (plankBBody == null) return;
            if (plankABody == plankBBody) return;
            _lastImpactTime = Time.time;
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
        private Rigidbody FindSecondBody(ContactPoint contact, Collider plankACollider)
        {
            Vector3 rayDirection = _currentVelocity.normalized;
            float rayDistance = _settings.raycastDistance;
            Vector3 rayOrigin = contact.point + rayDirection * 0.01f;
            Debug.DrawRay(rayOrigin, rayDirection * rayDistance, Color.red, 2f);
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
        private void CreateAttachment(Rigidbody plankA, Rigidbody plankB, ContactPoint contact)
        {
            FixedJoint joint = plankA.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = plankB;
            joint.anchor = plankA.transform.InverseTransformPoint(contact.point);
            joint.breakForce = _settings.jointBreakForce;
            joint.breakTorque = _settings.jointBreakTorque;
            IgnoreCollisionsBetween(plankA.gameObject, plankB.gameObject);
            SpawnNailVisual(contact, plankA.transform, plankA, plankB);
            _soundService?.PlayRandomSoundFxAtPosition(
                _settings.nailInClips, transform, _settings.sfxVolume);
            _hapticsService?.TriggerVibration(
                _settings.nailInHapticProfile, XRInteractionUtils.GetGrabbingHand(_grabInteractable));
            Debug.Log($"[NailHammer] Attached '{plankA.name}' ↔ '{plankB.name}' at {contact.point}");
            _eventService.Publish(new NailAttachedEvent(contact.point, plankA.gameObject, plankB.gameObject));
        }
        private void RemoveAttachment(Rigidbody bodyA, Rigidbody bodyB,
                                       FixedJoint joint, ContactPoint contact)
        {
            Destroy(joint);
            int remainingJointCount = CountJointsBetween(bodyA, bodyB, joint);
            if (remainingJointCount == 0)
            {
                RestoreCollisionsBetween(bodyA.gameObject, bodyB.gameObject);
            }
            DestroyClosestNailVisual(bodyA, bodyB, contact.point);
            _soundService?.PlayRandomSoundFxAtPosition(
                _settings.nailOutClips, transform, _settings.sfxVolume);
            _hapticsService?.TriggerVibration(
                _settings.nailOutHapticProfile, XRInteractionUtils.GetGrabbingHand(_grabInteractable));
            Debug.Log($"[NailHammer] Detached '{bodyA.name}' ↔ '{bodyB.name}' at {contact.point}");
            _eventService.Publish(new NailRemovedEvent(contact.point, bodyA.gameObject, bodyB.gameObject));
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
            // 2. Get the original scale of the prefab
            Vector3 prefabScale = _settings.nailPrefab.transform.localScale;

            // 3. Divide the prefab scale by the parent's global scale
            nail.transform.localScale = new Vector3(
                prefabScale.x / parent.lossyScale.x,
                prefabScale.y / parent.lossyScale.y,
                prefabScale.z / parent.lossyScale.z);

            NailVisualMarker marker = nail.AddComponent<NailVisualMarker>();
            marker.ConnectedBodyA = bodyA;
            marker.ConnectedBodyB = bodyB;
        }
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
    }
    public class NailVisualMarker : MonoBehaviour
    {
        [HideInInspector] public Rigidbody ConnectedBodyA;
        [HideInInspector] public Rigidbody ConnectedBodyB;
    }
}
