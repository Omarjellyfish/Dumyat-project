using UnityEngine;
#if XR_INTERACTION_TOOLKIT
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
#endif

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

#if XR_INTERACTION_TOOLKIT
        private XRBaseInteractable _interactable;
#endif

        private void Awake()
        {
            if (TryGetComponent<Collider>(out var col))
            {
                col.isTrigger = true;
            }

#if XR_INTERACTION_TOOLKIT
            if (TryGetComponent<XRBaseInteractable>(out _interactable))
            {
                _interactable.selectEntered.AddListener(OnStashGrabbed);
                _interactable.selectExited.AddListener(OnStashReleased);
            }
            else
            {
                Debug.LogError("InventoryStashZone: Missing XR Simple Interactable component! Please add an 'XR Simple Interactable' to this GameObject so it can be grabbed.");
            }
#endif
        }

        private void OnDestroy()
        {
#if XR_INTERACTION_TOOLKIT
            if (_interactable != null)
            {
                _interactable.selectEntered.RemoveListener(OnStashGrabbed);
                _interactable.selectExited.RemoveListener(OnStashReleased);
            }
#endif
        }

#if XR_INTERACTION_TOOLKIT
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
#endif

        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent<ItemPickup>(out var pickup))
            {
#if XR_INTERACTION_TOOLKIT
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
#else
                StashItem(pickup);
#endif
            }
        }

        private void OnTriggerExit(Collider other)
        {
#if XR_INTERACTION_TOOLKIT
            if (other.TryGetComponent<XRGrabInteractable>(out var grabInteractable))
            {
                grabInteractable.selectExited.RemoveListener(OnItemDroppedInZone);
            }
#endif
        }

#if XR_INTERACTION_TOOLKIT
        private void OnItemDroppedInZone(SelectExitEventArgs args)
        {
            if (args.interactableObject is XRGrabInteractable grabInteractable && 
                grabInteractable.TryGetComponent<ItemPickup>(out var pickup))
            {
                grabInteractable.selectExited.RemoveListener(OnItemDroppedInZone);
                StashItem(pickup);
            }
        }
#endif

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
