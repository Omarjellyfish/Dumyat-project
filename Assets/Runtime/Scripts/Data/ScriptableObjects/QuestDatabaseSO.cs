using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Central registry of all <see cref="QuestDefinitionSO"/> assets in the project.
    /// Injected into <see cref="QuestService"/> via Reflex so it can look up quest
    /// definitions by ID at runtime. Follows the same pattern as
    /// <see cref="RecipeDatabase"/> and <see cref="FishDatabase"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "QuestDatabase", menuName = "Dumyat/Quests/Quest Database", order = 1)]
    public class QuestDatabaseSO : ScriptableObject
    {
        [Tooltip("Every quest definition in the game. Populate via drag-and-drop in the Inspector.")]
        [SerializeField] private List<QuestDefinitionSO> _allQuests = new List<QuestDefinitionSO>();

        /// <summary>Read-only access to the full quest catalogue.</summary>
        public IReadOnlyList<QuestDefinitionSO> AllQuests => _allQuests;

        /// <summary>
        /// Finds a quest definition by its stable <see cref="QuestDefinitionSO.questID"/>.
        /// Returns <c>null</c> if not found.
        /// </summary>
        public QuestDefinitionSO GetByID(string questID)
        {
            if (string.IsNullOrEmpty(questID)) return null;

            for (int i = 0; i < _allQuests.Count; i++)
            {
                if (_allQuests[i] != null && _allQuests[i].questID == questID)
                    return _allQuests[i];
            }

            Debug.LogWarning($"QuestDatabaseSO: No quest found with ID '{questID}'.");
            return null;
        }
    }
}
