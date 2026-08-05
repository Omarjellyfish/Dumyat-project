using System;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// One "N of this item" line on a quest's reward list.
    ///
    /// Inline on <see cref="QuestDefinitionSO"/> rather than a <see cref="QuestRewardSO"/> asset,
    /// because handing out an item is the ordinary case and creating a separate asset per quest to
    /// express it is friction that leads to quests shipping with no reward at all. Anything more
    /// interesting than "give the player this" still belongs in a reward SO.
    /// </summary>
    [Serializable]
    public struct QuestItemReward
    {
        [Tooltip("Item to add to the player's inventory.")]
        public ItemData item;

        [Tooltip("How many to grant.")]
        [Min(1)]
        public int quantity;

        /// <summary>Whether this line actually grants something.</summary>
        public bool IsValid => item != null && quantity > 0;

        /// <summary>Quantity, floored at 1 — a line left at the struct default would otherwise grant none.</summary>
        public int EffectiveQuantity => Mathf.Max(1, quantity);
    }
}
