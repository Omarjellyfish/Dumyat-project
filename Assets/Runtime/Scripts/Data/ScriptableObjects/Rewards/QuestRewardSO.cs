using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Abstract base for quest rewards. Create concrete implementations via
    /// <c>Create ▸ Dumyat ▸ Quests ▸ Rewards</c> and drag them into
    /// <see cref="QuestDefinitionSO.reward"/>.
    /// </summary>
    public abstract class QuestRewardSO : ScriptableObject
    {
        /// <summary>
        /// Called by <see cref="QuestService"/> when the owning quest completes.
        /// Use the <paramref name="context"/> to interact with game services.
        /// </summary>
        public abstract void GrantReward(QuestRewardContext context);
    }
}
