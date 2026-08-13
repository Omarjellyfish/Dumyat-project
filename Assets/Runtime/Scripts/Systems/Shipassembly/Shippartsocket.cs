using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// Sits alongside a built-in <see cref="XRSocketInteractor"/> on a single ship-assembly socket.
    /// Handles the three things XRI's socket doesn't do out of the box:
    ///   1. Enforcing assembly ORDER -- this socket refuses to accept a part until the
    ///      socket(s) listed in <see cref="_requiredPreviousSockets"/> are already filled.
    ///   2. Locking the part in place once socketed, so the player can't yank it back out.
    ///   3. Reporting fill/empty state to <see cref="ShipAssemblyManager"/>.
    ///
    /// The "only the matching part fits this socket" restriction is still NOT done here -- that's
    /// configured entirely in the Editor via the XR Socket Interactor's Interaction Layer Mask.
    /// Order enforcement is a separate, additional gate on top of that.
    /// </summary>
    [RequireComponent(typeof(XRSocketInteractor))]
    public class ShipPartSocket : MonoBehaviour, IXRSelectFilter
    {
        [Header("Identification")]
        [Tooltip("Human-readable ID for this socket, e.g. \"Mast\", \"Hull\", \"Deck\". Used for logging/lookup only -- the actual part-matching restriction lives on the Interaction Layer Mask.")]
        [SerializeField] private string _socketID;

        [Header("Assembly Order")]
        [Tooltip("Sockets that must ALL be filled before this socket will accept a part. Leave empty if this socket has no prerequisite (e.g. it's part 1).")]
        [SerializeField] private ShipPartSocket[] _requiredPreviousSockets;

        [Header("Optional Visual Feedback")]
        [Tooltip("Enabled/disabled to indicate a valid part is hovering. Leave null to skip.")]
        [SerializeField] private GameObject _hoverHighlight;

        [Tooltip("Optional: shown/hidden to indicate this socket is locked because earlier parts aren't placed yet.")]
        [SerializeField] private GameObject _lockedIndicator;

        private XRSocketInteractor _socketInteractor;
        private XRGrabInteractable _socketedGrabInteractable;
        private InteractionLayerMask _originalInteractionLayers;
        private IHapticService _haptics;

        [Inject]
        private void Inject(IHapticService haptics)
        {
            _haptics = haptics;
        }

        // Parts currently hovering this socket that we're watching for release, so a
        // rejected one (wrong order) can be snapped back to its ShipPartSpawnPoint.
        private readonly HashSet<XRGrabInteractable> _watchedParts = new();

        public string SocketID => _socketID;
        public bool IsFilled { get; private set; }

        /// <summary>True once every socket in <see cref="_requiredPreviousSockets"/> is filled.</summary>
        public bool IsUnlocked
        {
            get
            {
                if (_requiredPreviousSockets == null) return true;
                foreach (var prev in _requiredPreviousSockets)
                {
                    if (prev != null && !prev.IsFilled)
                        return false;
                }
                return true;
            }
        }

        public bool canProcess => isActiveAndEnabled;

        private void Awake()
        {
            _socketInteractor = GetComponent<XRSocketInteractor>();
        }

        private void OnEnable()
        {
            _socketInteractor.selectEntered.AddListener(OnPartSocketed);
            _socketInteractor.selectExited.AddListener(OnPartUnsocketed);
            _socketInteractor.selectFilters.Add(this);

            _socketInteractor.hoverEntered.AddListener(OnHoverEntered);
            _socketInteractor.hoverExited.AddListener(OnHoverExited);

            RefreshLockedIndicator();
        }

        private void OnDisable()
        {
            _socketInteractor.selectEntered.RemoveListener(OnPartSocketed);
            _socketInteractor.selectExited.RemoveListener(OnPartUnsocketed);
            _socketInteractor.selectFilters.Remove(this);

            _socketInteractor.hoverEntered.RemoveListener(OnHoverEntered);
            _socketInteractor.hoverExited.RemoveListener(OnHoverExited);

            foreach (var grab in _watchedParts)
            {
                if (grab != null)
                    grab.selectExited.RemoveListener(OnHeldPartReleased);
            }
            _watchedParts.Clear();
        }

        /// <summary>
        /// IXRSelectFilter.Process -- called by XRI before it lets this interactor select anything.
        /// Returning false here means "this interactor cannot select that interactable right now",
        /// so a part physically dropped into this socket just won't be accepted until it's unlocked.
        /// </summary>
        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            return IsUnlocked;
        }

        private void RefreshLockedIndicator()
        {
            if (_lockedIndicator != null)
                _lockedIndicator.SetActive(!IsUnlocked);
        }

        private void Update()
        {
            // Cheap and simple: keep the locked indicator honest as previous sockets fill in.
            // If you'd rather avoid a per-frame check, call RefreshLockedIndicator() from an
            // event handler subscribed to each previous socket's fill instead.
            if (_lockedIndicator != null)
                RefreshLockedIndicator();
        }

        private void OnPartSocketed(SelectEnterEventArgs args)
        {
            if (IsFilled) return; // guard against re-entrant/duplicate select events

            IsFilled = true;

            if (_hoverHighlight != null)
                _hoverHighlight.SetActive(false);

            if (args.interactableObject is XRGrabInteractable grabInteractable)
            {
                _socketedGrabInteractable = grabInteractable;
                _originalInteractionLayers = grabInteractable.interactionLayers;
                grabInteractable.interactionLayers = 0;
            }
            else
            {
                Debug.LogWarning($"ShipPartSocket '{_socketID}': socketed object has no XRGrabInteractable to lock.");
            }
            
            _haptics?.TriggerVibration(0.4f, 0.15f, 0.5f, HapticHand.Both);

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

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            // Don't invite a hover highlight for a socket that's still locked by order.
            if (IsUnlocked && _hoverHighlight != null)
                _hoverHighlight.SetActive(true);

            WatchForRelease(args.interactableObject);
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
            // Don't turn the highlight back off if a part just got socketed (already handled above)
            if (!IsFilled && _hoverHighlight != null)
                _hoverHighlight.SetActive(false);
        }

        /// <summary>
        /// Starts watching a part that's hovering this socket, so it can be snapped back to its
        /// <see cref="ShipPartSpawnPoint"/> if this socket ends up rejecting it.
        /// </summary>
        private void WatchForRelease(IXRInteractable interactable)
        {
            if (interactable is not XRGrabInteractable grab) return;
            if (!_watchedParts.Add(grab)) return; // already watching it

            grab.selectExited.AddListener(OnHeldPartReleased);
        }

        /// <summary>
        /// Called when a watched part gets released (dropped) by the player's hand. If it never
        /// got accepted by this socket (order-locked or otherwise rejected), snap it back to its
        /// <see cref="ShipPartSpawnPoint"/>.
        /// </summary>
        private void OnHeldPartReleased(SelectExitEventArgs args)
        {
            if (args.interactableObject is not XRGrabInteractable grab) return;
            if (!_watchedParts.Contains(grab)) return;

            // Defer a frame: right after the hand releases, XRI may still be in the middle of
            // handing the part to this (or another) socket's select. Checking immediately would
            // race against that and snap back a part that's actually about to be validly socketed.
            StartCoroutine(ResolveReleaseNextFrame(grab));
        }

        private System.Collections.IEnumerator ResolveReleaseNextFrame(XRGrabInteractable grab)
        {
            yield return null;

            if (!_watchedParts.Contains(grab)) yield break;

            // Made it into this socket -- correctly placed, leave it.
            if (_socketedGrabInteractable == grab)
            {
                _watchedParts.Remove(grab);
                grab.selectExited.RemoveListener(OnHeldPartReleased);
                yield break;
            }

            // Being held by a hand or sitting in some other socket -- don't yank it.
            if (grab.interactorsSelecting.Count > 0)
                yield break;

            var spawnPoint = grab.GetComponent<ShipPartSpawnPoint>();
            if (spawnPoint == null)
            {
                Debug.LogWarning($"ShipPartSocket '{_socketID}': '{grab.name}' has no ShipPartSpawnPoint, can't snap it back.");
            }
            else
            {
                var t = grab.transform;
                t.SetParent(spawnPoint.SpawnParent, true);
                t.SetPositionAndRotation(spawnPoint.SpawnPosition, spawnPoint.SpawnRotation);

                var rb = grab.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }

            _watchedParts.Remove(grab);
            grab.selectExited.RemoveListener(OnHeldPartReleased);
        }
    }
}