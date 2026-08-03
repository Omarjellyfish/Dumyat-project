using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Quest reward that credits the player's EGP balance when the quest completes.
    /// Create via <c>Create ▸ Dumyat ▸ Quests ▸ Rewards ▸ Grant Money</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "NewMoneyReward", menuName = "Dumyat/Quests/Rewards/Grant Money")]
    public class GrantMoneyRewardSO : QuestRewardSO
    {
        [Tooltip("EGP to credit when the quest completes. Whole Egyptian Pounds.")]
        [Min(1)]
        [SerializeField] private int _amount = 100;

        public override void GrantReward(QuestRewardContext context)
        {
            if (context.WalletService == null)
            {
                Debug.LogWarning("[GrantMoneyReward] No wallet service in the reward context — skipping.");
                return;
            }

            context.WalletService.Add(_amount, "Quest reward");
        }
    }
}
