using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Quest reward that unlocks a crafting blueprint when the quest completes.
    /// Create via <c>Create ▸ Dumyat ▸ Quests ▸ Rewards ▸ Unlock Blueprint</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "NewBlueprintReward", menuName = "Dumyat/Quests/Rewards/Unlock Blueprint")]
    public class UnlockBlueprintRewardSO : QuestRewardSO
    {
        [Tooltip("The crafting recipe to unlock when the quest completes.")]
        [SerializeField] private CraftingRecipe _recipe;

        public override void GrantReward(QuestRewardContext context)
        {
            if (_recipe == null)
            {
                Debug.LogWarning("[UnlockBlueprintReward] No recipe assigned — skipping.");
                return;
            }

            context.CraftingService.UnlockBlueprint(_recipe.RecipeName);
            Debug.Log($"[UnlockBlueprintReward] Unlocked blueprint: '{_recipe.RecipeName}'");
        }
    }
}
