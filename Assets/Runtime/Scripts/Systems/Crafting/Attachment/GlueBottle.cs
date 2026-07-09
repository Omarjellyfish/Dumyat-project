using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Reflex.Attributes;

namespace DumyatRuntime
{
    [RequireComponent(typeof(XRGrabInteractable))]
    public class GlueBottle : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private GameObject _glueBlobPrefab;
        [SerializeField] private Transform _nozzleTransform;
        [SerializeField] private float _squeezeCooldown = 0.5f;
        [SerializeField] private LayerMask _woodLayerMask; // Set this to your StructuralPiece layer

        private XRGrabInteractable _grabInteractable;
        private float _lastSqueezeTime;

        private IEventService _eventService;

        [Inject]
        private void Inject(IEventService eventService) => _eventService = eventService;

        private void Awake()
        {
            _grabInteractable = GetComponent<XRGrabInteractable>();
        }

        private void OnEnable()
        {
            _grabInteractable.activated.AddListener(HandleActivated);
        }

        private void OnDisable()
        {
            _grabInteractable.activated.RemoveListener(HandleActivated);
        }

        private void HandleActivated(ActivateEventArgs args)
        {
            if (Time.time < _lastSqueezeTime + _squeezeCooldown) return;

            ApplyGlue();
        }

        private void ApplyGlue()
        {
            // Raycast slightly forward from the nozzle to find the wood
            if (Physics.Raycast(_nozzleTransform.position, _nozzleTransform.up, out RaycastHit hit, 0.1f, _woodLayerMask))
            {
                Rigidbody hitBody = hit.collider.attachedRigidbody;

                if (hitBody != null && hitBody.GetComponent<StructuralPiece>() != null)
                {
                    _lastSqueezeTime = Time.time;

                    // Spawn the glue blob
                    GameObject glueInstance = Instantiate(_glueBlobPrefab, hit.point, Quaternion.LookRotation(hit.normal), hitBody.transform);

                    // Assign the Rigidbody so the glue knows what it's attached to
                    GlueBlob glueBlob = glueInstance.GetComponent<GlueBlob>();
                    if (glueBlob != null)
                    {
                        glueBlob.AttachedBodyA = hitBody;
                    }

                    _eventService.Publish(new GlueAppliedEvent(hit.point, hitBody.gameObject));
                }
            }
        }
    }
}