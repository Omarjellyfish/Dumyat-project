using UnityEngine;

namespace DumyatRuntime
{
    public class ItemSpawner : MonoBehaviour, IManager
    {
        public static ItemSpawner Singleton { get; private set; }

        private void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }
            Singleton = this;
        }

        private void OnEnable()
        {
            InventoryManager.OnItemDropped += HandlePlayerDrop;
        }

        private void OnDisable()
        {
            InventoryManager.OnItemDropped -= HandlePlayerDrop;
        }

        public GameObject SpawnItem(string itemID, Vector3 spawnPosition, Quaternion spawnRotation, int quantity = 1)
        {
            if (InventoryManager.Singleton.ItemDatabase == null) return null;

            ItemData blueprint = InventoryManager.Singleton.ItemDatabase.GetItemByID(itemID);

            if (blueprint == null || blueprint.DropPrefab == null)
            {
                Debug.LogError($"ItemSpawner: Failed to spawn '{itemID}'. Missing from database or no prefab.");
                return null;
            }

            GameObject spawnedInstance = Instantiate(blueprint.DropPrefab, spawnPosition, spawnRotation);

            if (spawnedInstance.TryGetComponent<ItemPickup>(out ItemPickup pickup))
            {
                pickup.ItemID = itemID;
                pickup.Amount = quantity;
            }

            return spawnedInstance;
        }

        private void HandlePlayerDrop(ItemData blueprint, int quantity)
        {
            Transform playerHead = Camera.main.transform;

            Vector3 dropPosition = playerHead.position + (playerHead.forward * 0.5f);

            SpawnItem(blueprint.ItemID, dropPosition, Quaternion.identity, quantity);

            Debug.Log($"ItemSpawner: Player dropped {quantity}x {blueprint.ItemID}");
        }
    }
}