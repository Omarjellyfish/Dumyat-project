using UnityEngine;
using Reflex.Attributes;
using Reflex.Extensions;
using Reflex.Injectors;

namespace DumyatRuntime
{
    public class ItemSpawner : MonoBehaviour
    {
        private IInventoryService _inventory;

        [Inject]
        private void Inject(IInventoryService inventory) => _inventory = inventory;

        private void OnEnable()
        {
            EventBus.Subscribe<ItemDroppedEvent>(HandlePlayerDrop);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<ItemDroppedEvent>(HandlePlayerDrop);
        }

        public GameObject SpawnItem(string itemID, Vector3 spawnPosition, Quaternion spawnRotation, int quantity = 1)
        {
            if (_inventory == null || _inventory.ItemDatabase == null) return null;

            ItemData blueprint = _inventory.ItemDatabase.GetItemByID(itemID);

            if (blueprint == null || blueprint.DropPrefab == null)
            {
                Debug.LogError($"ItemSpawner: Failed to spawn '{itemID}'. Missing from database or no prefab.");
                return null;
            }

            GameObject spawnedInstance = Instantiate(blueprint.DropPrefab, spawnPosition, spawnRotation);

            // Runtime-spawned objects are not auto-injected; inject so the item's ItemPickup receives
            // its IInventoryService/IHapticService. (Alternative: add GameObjectSelfInjector to the prefab.)
            GameObjectInjector.InjectRecursive(spawnedInstance, gameObject.scene.GetSceneContainer());

            if (spawnedInstance.TryGetComponent<ItemPickup>(out ItemPickup pickup))
            {
                pickup.ItemID = itemID;
                pickup.Amount = quantity;
            }

            return spawnedInstance;
        }

        private void HandlePlayerDrop(ItemDroppedEvent evt)
        {
            ItemData blueprint = evt.Item;
            int quantity = evt.Amount;

            Transform playerHead = Camera.main.transform;

            Vector3 dropPosition = playerHead.position + (playerHead.forward * 0.5f);

            SpawnItem(blueprint.ItemID, dropPosition, Quaternion.identity, quantity);

            Debug.Log($"ItemSpawner: Player dropped {quantity}x {blueprint.ItemID}");
        }
    }
}