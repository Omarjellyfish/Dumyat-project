using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace DumyatRuntime
{
    /// <summary>
    /// Sits alongside a built-in <see cref="XRSocketInteractor"/> on a single ship-assembly socket.
    /// Handles the two things XRI's socket doesn't do out of the box:
    ///   1. Locking the part in place once socketed, so the player can't yank it back out.
    ///   2. Reporting fill/empty state to <see cref="ShipAssemblyManager"/>.
    ///
    /// The "only the matching part fits this socket" restriction is NOT done here -- it's
    /// configured entirely in the Editor via the XR Socket Interactor's Interaction Layer Mask.
    /// See the setup notes that shipped alongside this script.
    /// </summary>
    [RequireComponent(typeof(XRSocketInteractor))]
    public class ShipPartSocket : MonoBehaviour
    {
        [Header("Identification")]
        [Tooltip("Human-readable ID for this socket, e.g. \"Mast\", \"Hull\", \"Deck\". Used for logging/lookup only -- the actual part-matching restriction lives on the Interaction Layer Mask.")]
        [SerializeField] private string _socketID;

        [Header("Optional Visual Feedback")]
        [Tooltip("Enabled/disabled to indicate a valid part is hovering. Leave null to skip.")]
        [SerializeField] private GameObject _hoverHighlight;

        private XRSocketInteractor _socketInteractor;
        private XRGrabInteractable _socketedGrabInteractable;
        private InteractionLayerMask _originalInteractionLayers;

        public string SocketID => _socketID;
        public bool IsFilled { get; private set; }

        private void Awake()
        {
            _socketInteractor = GetComponent<XRSocketInteractor>();
        }

        private void OnEnable()
        {
            _socketInteractor.selectEntered.AddListener(OnPartSocketed);
            _socketInteractor.selectExited.AddListener(OnPartUnsocketed);

            if (_hoverHighlight != null)
            {
                _socketInteractor.hoverEntered.AddListener(OnHoverEntered);
                _socketInteractor.hoverExited.AddListener(OnHoverExited);
            }
        }

        private void OnDisable()
        {
            _socketInteractor.selectEntered.RemoveListener(OnPartSocketed);
            _socketInteractor.selectExited.RemoveListener(OnPartUnsocketed);

            if (_hoverHighlight != null)
            {
                _socketInteractor.hoverEntered.RemoveListener(OnHoverEntered);
                _socketInteractor.hoverExited.RemoveListener(OnHoverExited);
            }
        }

        private void OnPartSocketed(SelectEnterEventArgs args)
        {
            IsFilled = true;

            if (_hoverHighlight != null)
                _hoverHighlight.SetActive(false);

            if (args.interactableObject is XRGrabInteractable grabInteractable)
            {
                _socketedGrabInteractable = grabInteractable;
                _originalInteractionLayers = grabInteractable.interactionLayers;

                // IMPORTANT: we do NOT set grabInteractable.enabled = false here. Disabling an
                // XRBaseInteractable forces the interaction manager to cancel its current
                // selection (i.e. it would immediately pop back OUT of the socket). Instead we
                // strip its interaction layers so no hand/ray interactor can select it anymore,
                // while leaving the component enabled so the socket's own selection is undisturbed.
                grabInteractable.interactionLayers = 0;
            }
            else
            {
                Debug.LogWarning($"ShipPartSocket '{_socketID}': socketed object has no XRGrabInteractable to lock.");
            }

            ShipAssemblyManager.Instance?.NotifySocketFilled(this);
        }

        private void OnPartUnsocketed(SelectExitEventArgs args)
        {
            // Under normal play this shouldn't happen (parts are locked in on socket), but it's
            // handled correctly in case a socket is ever cleared programmatically (e.g. a reset/
            // undo flow), so state doesn't get out of sync with ShipAssemblyManager.
            IsFilled = false;

            if (_socketedGrabInteractable != null)
            {
                _socketedGrabInteractable.interactionLayers = _originalInteractionLayers;
                _socketedGrabInteractable = null;
            }

            ShipAssemblyManager.Instance?.NotifySocketEmptied(this);
        }

        private void OnHoverEntered(HoverEnterEventArgs args) => _hoverHighlight.SetActive(true);

        private void OnHoverExited(HoverExitEventArgs args)
        {
            // Don't turn the highlight back off if a part just got socketed (already handled above)
            if (!IsFilled)
                _hoverHighlight.SetActive(false);
        }
    }
}