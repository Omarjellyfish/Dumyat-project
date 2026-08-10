using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit;
using Reflex.Attributes;



namespace DumyatRuntime
{
    [RequireComponent(typeof(Collider))]
    public class InventoryStashZone : MonoBehaviour
    {
        [Header("UI Reference")]
        [SerializeField] private InventoryToolkitUI _inventoryUI;

        private IHapticService _haptics;
        private ISoundService _sound;

        [Inject]
        private void Inject(IHapticService haptics, ISoundService sound)
        {
            _haptics = haptics;
            _sound = sound;
        }

        [Header("Audio Feedback")]
        [Tooltip("Played through ISoundService (pooled, on the SFX bus) when an item is stashed.")]
        [SerializeField] private AudioClip _stashSound;

        [Range(0f, 1f)]
        [SerializeField] private float _stashVolume = 1f;


        private XRGrabInteractable _interactable;


        private void Awake()
        {
            // The panel is deliberately NOT resolved here — see ResolveInventoryUI. Awake is too
            // early in every shipping scene.

            if (TryGetComponent<XRGrabInteractable>(out _interactable))
            {
                _interactable.selectEntered.AddListener(OnStashGrabbed);
                _interactable.selectExited.AddListener(OnStashReleased);
            }
            else
            {
                Debug.LogError("InventoryStashZone: Missing XR Grab Interactable component! Please add an 'XR Grab Interactable' to this GameObject so it can be grabbed.");
            }

        }

        private void OnDestroy()
        {

            if (_interactable != null)
            {
                _interactable.selectEntered.RemoveListener(OnStashGrabbed);
                _interactable.selectExited.RemoveListener(OnStashReleased);
            }

        }

        private void OnStashGrabbed(SelectEnterEventArgs args)
        {
            InventoryToolkitUI ui = ResolveInventoryUI();

            if (ui == null)
            {
                Debug.LogError("InventoryStashZone: grabbed, but there is no ACTIVE InventoryToolkitUI " +
                               "to open. It rides on the player rig, so the question is whether this " +
                               "scene has a live one.", this);
                return;
            }

            // Logged on the way through, not just on failure. "I grabbed it and nothing happened" has
            // two very different causes — the grab never registered, or it did and the panel refused
            // to draw — and without a line here the console looks identical either way.
            ui.ToggleUI();
            VerboseLog.Trace($"InventoryStashZone: stash grabbed — toggled '{ui.name}'.", this);
        }

        /// <summary>
        /// The bag panel, found on first use and remembered.
        ///
        /// Resolved HERE rather than in Awake, which is where it used to be and could never work: no
        /// shipping scene authors an active rig, so the panel arrives with the one
        /// <see cref="PlayerRigSpawner"/> instantiates on <c>SceneLoadCompleteEvent</c> — published
        /// after every Awake in the new scene has already run. Ezbet Al Bourg makes it worst, because
        /// it DOES author a rig but leaves it disabled on purpose, and the parameterless
        /// <c>FindFirstObjectByType</c> skips inactive objects, so the lookup found nothing there
        /// either. Grabbing the stash then logged "reference is missing in the Inspector", which sent
        /// people to the Inspector to fix a timing problem.
        ///
        /// A grab happens long after the rig exists, so by the time this is asked the answer is real.
        /// Inactive objects stay excluded deliberately: the disabled authored rig carries a panel too,
        /// and picking that one would toggle a bag nested under a switched-off rig — nothing visible.
        /// </summary>
        private InventoryToolkitUI ResolveInventoryUI()
        {
            if (_inventoryUI != null) return _inventoryUI;

            _inventoryUI = FindFirstObjectByType<InventoryToolkitUI>();
            return _inventoryUI;
        }

        private void OnStashReleased(SelectExitEventArgs args)
        {
            VerboseLog.Trace("InventoryStashZone: Stash was released.");
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent<ItemPickup>(out var pickup))
            {
                if (pickup.TryGetComponent<XRGrabInteractable>(out var grabInteractable))
                {
                    if (!grabInteractable.isSelected)
                    {
                        StashItem(pickup);
                    }
                    else
                    {
                        grabInteractable.selectExited.AddListener(OnItemDroppedInZone);
                    }
                }
                else
                {
                    StashItem(pickup);
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {

            if (other.TryGetComponent<XRGrabInteractable>(out var grabInteractable))
            {
                grabInteractable.selectExited.RemoveListener(OnItemDroppedInZone);
            }

        }


        private void OnItemDroppedInZone(SelectExitEventArgs args)
        {
            if (args.interactableObject is XRGrabInteractable grabInteractable &&
                grabInteractable.TryGetComponent<ItemPickup>(out var pickup))
            {
                grabInteractable.selectExited.RemoveListener(OnItemDroppedInZone);
                StashItem(pickup);
            }
        }


        private void StashItem(ItemPickup pickup)
        {
            if (pickup == null) return;

            // Pooled + routed to the SFX mixer bus, so the settings volume slider applies.
            if (_stashSound != null) _sound?.PlaySoundFxAtPosition(_stashSound, transform, _stashVolume);

            _haptics?.TriggerVibration(0.6f, 0.15f, 0.8f, HapticHand.Both);

            //VerboseLog.Trace($"InventoryStashZone: Physical item '{pickup.ItemID}' stashed.");

            pickup.Collect();
        }
    }
}
