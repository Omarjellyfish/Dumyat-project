using UnityEngine;
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

            if (InventoryManager.Singleton != null)
            {
                InventoryManager.Singleton.AddItemToInventory(_itemID, _amount);
                
                if (HapticFeedback.Singleton != null)
                {
                    HapticFeedback.Singleton.TriggerVibration(0.4f, 0.1f, 0.5f, HapticHand.Both);
                }

                Destroy(gameObject);
            }
        }
    }
}
