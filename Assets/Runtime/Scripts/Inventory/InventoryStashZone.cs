using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit;



namespace DumyatRuntime
{
    [RequireComponent(typeof(Collider))]
    public class InventoryStashZone : MonoBehaviour
    {
        [Header("UI Reference")]
        [SerializeField] private InventoryToolkitUI _inventoryUI;

        [Header("Audio Feedback")]
        [SerializeField] private AudioClip _stashSound;
        [SerializeField] private AudioSource _audioSource;


        private XRGrabInteractable _interactable;


        private void Awake()
        {
            if (_inventoryUI == null)
            {
                _inventoryUI = FindFirstObjectByType<InventoryToolkitUI>();
                if (_inventoryUI == null)
                {
                    Debug.LogWarning("InventoryStashZone: Could not find InventoryToolkitUI in the scene automatically!");
                }
            }

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
            Debug.Log("InventoryStashZone: Stash was grabbed!");

            if (_inventoryUI != null)
            {
                _inventoryUI.OpenUI();
                Debug.Log("InventoryStashZone: Opened UI successfully.");
            }
            else
            {
                Debug.LogError("InventoryStashZone: Cannot open UI! _inventoryUI reference is missing in the Inspector.");
            }
        }

        private void OnStashReleased(SelectExitEventArgs args)
        {
            Debug.Log("InventoryStashZone: Stash was released.");

            if (_inventoryUI != null)
            {
                _inventoryUI.CloseUI();
            }
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

                StashItem(pickup);

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

            if (_audioSource != null && _stashSound != null)
            {
                _audioSource.PlayOneShot(_stashSound);
            }

            if (HapticFeedback.Singleton != null)
            {
                HapticFeedback.Singleton.TriggerVibration(0.6f, 0.15f, 0.8f, HapticHand.Both);
            }

            Debug.Log($"InventoryStashZone: Physical item '{pickup.ItemID}' stashed.");

            pickup.Collect();
        }
    }
}
