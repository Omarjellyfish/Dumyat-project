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

        [Header("Filters (leave empty for 'any')")]
        [Tooltip("Optional name filter — e.g. a specific fish name, recipe name, or item name. " +
                 "If empty, any event of the trigger type counts.")]
        public string targetName;

        [Header("Rewards")]
        [Tooltip("Player-facing reward text (actual reward granting is a future extension).")]
        public string rewardDescription;
    }
}
