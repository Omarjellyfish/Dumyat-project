using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Authoring asset for a single quest. Designers create one of these per quest
    /// via <c>Create ▸ Dumyat ▸ Quests ▸ Quest Definition</c> and populate it in the Inspector.
    /// The <see cref="QuestService"/> reads these at runtime through a <see cref="QuestDatabaseSO"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "NewQuest", menuName = "Dumyat/Quests/Quest Definition", order = 0)]
    public class QuestDefinitionSO : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Unique, stable key used in save data. Must never change once shipped.")]
        public string questID;

        [Tooltip("Player-facing quest name shown in the UI.")]
        public string displayName;

        [TextArea(2, 5)]
        [Tooltip("Player-facing description explaining the objective.")]
        public string description;

        [Tooltip("The scene this quest belongs to (exact Build Settings name). " +
                 "Quest auto-activates when this scene loads.")]
        public string sceneName;

        [Header("Objective")]
        [Tooltip("Which gameplay event drives this quest's progress.")]
        public QuestTriggerType triggerType;

        [Tooltip("How many times the trigger must fire to complete the quest.")]
        [Min(1)]
        public int targetCount = 1;

        [Header("Target (leave empty for 'any')")]
        [Tooltip("Drag any ScriptableObject that implements IQuestTarget (ItemData, CraftingRecipe). " +
                 "The service reads TargetID from this to filter events. Leave empty to match any event of the trigger type.")]
        public ScriptableObject targetAsset;

        [Tooltip("Fallback string filter for targets that aren't ScriptableObjects (e.g. fish names). " +
                 "Ignored if targetAsset is set.")]
        public string targetNameFallback;

        [Header("Rewards")]
        [Tooltip("Player-facing reward text.")]
        public string rewardDescription;

        [Tooltip("Optional reward to grant on completion. Create via Dumyat > Quests > Rewards.")]
        public QuestRewardSO reward;

        [Header("Persistence")]
        [Tooltip("Optional persistence keys to unlock when this quest completes.")]
        public List<string> persistenceKeysOnComplete = new List<string>();
    }
}
