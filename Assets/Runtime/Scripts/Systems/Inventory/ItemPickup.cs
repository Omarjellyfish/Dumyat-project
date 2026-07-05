using UnityEngine;
using Reflex.Attributes;
#if XR_INTERACTION_TOOLKIT
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
#endif

namespace DumyatRuntime
{
    public class ItemPickup : MonoBehaviour
    {
        [Header("Item Configuration")]
        [SerializeField] private string _itemID;
        [SerializeField] private int _amount = 1;

        private IInventoryService _inventory;
        private IHapticService _haptics;

        // Injected by Reflex: for scene items automatically, and for runtime-spawned items by
        // ItemSpawner (which injects the instance against the scene container).
        [Inject]
        private void Inject(IInventoryService inventory, IHapticService haptics)
        {
            _inventory = inventory;
            _haptics = haptics;
        }

        public string ItemID
        {
            get => _itemID;
            set => _itemID = value;
        }

        public int Amount
        {
            get => _amount;
            set => _amount = value;
        }

        public void Collect()
        {
            if (string.IsNullOrEmpty(_itemID))
            {
                Debug.LogWarning($"ItemPickup: Cannot collect item on {gameObject.name} because ItemID is empty.");
                return;
            }

            if (_inventory == null)
            {
                Debug.LogWarning($"ItemPickup: No IInventoryService injected on {gameObject.name}; " +
                                 "ensure the scene has a ContainerScope, or that ItemSpawner injects spawned items.");
                return;
            }

            _inventory.AddItemToInventory(_itemID, _amount);

            _haptics?.TriggerVibration(0.4f, 0.1f, 0.5f, HapticHand.Both);

            Destroy(gameObject);
        }
    }
}
