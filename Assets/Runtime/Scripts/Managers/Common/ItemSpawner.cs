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

        /// <summary>
        /// THE UNIVERSAL SPAWN METHOD. 
        /// Notice we added "int quantity = 1" so we don't lose stack data!
        /// </summary>
        public GameObject SpawnItem(string itemID, Vector3 spawnPosition, Quaternion spawnRotation, int quantity = 1)
        {
            if (InventoryManager.Singleton.itemDatabase == null) return null;

            ItemData blueprint = InventoryManager.Singleton.itemDatabase.GetItemByID(itemID);

            if (blueprint == null || blueprint.dropPrefab == null)
            {
                Debug.LogError($"ItemSpawner: Failed to spawn '{itemID}'. Missing from database or no prefab.");
                return null;
            }

            GameObject spawnedInstance = Instantiate(blueprint.dropPrefab, spawnPosition, spawnRotation);

            // FIX: Re-inject both the ID AND the Quantity!
            if (spawnedInstance.TryGetComponent<ItemPickup>(out ItemPickup pickup))
            {
                pickup.itemID = itemID;
                pickup.amount = quantity;
            }

            return spawnedInstance;
        }

        /// <summary>
        /// This specific method just bridges the Player's Inventory Drop event
        /// </summary>
        private void HandlePlayerDrop(string itemID, int quantity)
        {
            // FIX: Get the VR player's headset position, NOT the Manager's position.
            // (Assuming your VR headset is tagged as MainCamera)
            Transform playerHead = Camera.main.transform;

            // Spawn it 0.5 meters in front of the player's face/chest
            Vector3 dropPosition = playerHead.position + (playerHead.forward * 0.5f);

            // Pass the quantity along to the universal method!
            SpawnItem(itemID, dropPosition, Quaternion.identity, quantity);

            Debug.Log($"ItemSpawner: Player dropped {quantity}x {itemID}");
        }
    }
}