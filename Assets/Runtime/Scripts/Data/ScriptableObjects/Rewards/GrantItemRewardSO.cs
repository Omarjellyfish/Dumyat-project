using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Quest reward that adds items to the player's inventory when the quest completes.
    /// Create via <c>Create ▸ Dumyat ▸ Quests ▸ Rewards ▸ Grant Item</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "NewItemReward", menuName = "Dumyat/Quests/Rewards/Grant Item")]
    public class GrantItemRewardSO : QuestRewardSO
    {
        [Tooltip("The item to add to the player's inventory.")]
        [SerializeField] private ItemData _item;

        [Tooltip("How many of this item to grant.")]
        [Min(1)]
        [SerializeField] private int _quantity = 1;

        public override void GrantReward(QuestRewardContext context)
        {
            if (_item == null)
            {
                Debug.LogWarning("[GrantItemReward] No item assigned — skipping.");
                return;
            }

            context.InventoryService.AddItemToInventory(_item.ItemID, _quantity);
            Debug.Log($"[GrantItemReward] Granted {_quantity}x '{_item.DisplayName}' to inventory.");
        }
    }
}
