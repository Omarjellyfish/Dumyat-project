using System.Collections.Generic;

namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for the quest tracking system. Registered as a singleton
    /// in <see cref="GameInstaller"/> and resolved by UI, NPC triggers, and any
    /// system that needs to query or drive quest state.
    /// </summary>
    public interface IQuestService
    {
        /// <summary>Marks a quest as active and begins tracking its trigger events.</summary>
        void ActivateQuest(string questID);

        /// <summary>Returns <c>true</c> if the quest is currently being tracked.</summary>
        bool IsQuestActive(string questID);

        /// <summary>Returns <c>true</c> if the quest has been completed (ever).</summary>
        bool IsQuestCompleted(string questID);

        /// <summary>Returns the current progress counter for the given quest (0 if untracked).</summary>
        int GetProgress(string questID);

        /// <summary>Looks up the authoring asset for a quest. Returns <c>null</c> if not found.</summary>
        QuestDefinitionSO GetDefinition(string questID);

        /// <summary>Returns definitions for all currently active quests.</summary>
        List<QuestDefinitionSO> GetActiveQuests();

        /// <summary>Returns definitions for all completed quests.</summary>
        List<QuestDefinitionSO> GetCompletedQuests();
    }
}
