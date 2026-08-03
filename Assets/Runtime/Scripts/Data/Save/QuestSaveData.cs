using System;
using System.Collections.Generic;

namespace DumyatRuntime
{
    [Serializable]
    public class QuestSaveData
    {
        public List<string> activeQuestIDs = new List<string>();
        public List<string> completedQuestIDs = new List<string>();

        // Per-quest progress — parallel lists because JsonUtility cannot serialize Dictionary.
        // Index N in progressQuestIDs corresponds to index N in progressValues.
        public List<string> progressQuestIDs = new List<string>();
        public List<int> progressValues = new List<int>();

        /// <summary>Returns the saved progress for <paramref name="questID"/>, or 0 if untracked.</summary>
        public int GetProgress(string questID)
        {
            int index = progressQuestIDs.IndexOf(questID);
            return index >= 0 ? progressValues[index] : 0;
        }

        /// <summary>Sets the progress counter for <paramref name="questID"/>, creating the entry if needed.</summary>
        public void SetProgress(string questID, int value)
        {
            int index = progressQuestIDs.IndexOf(questID);
            if (index >= 0)
            {
                progressValues[index] = value;
            }
            else
            {
                progressQuestIDs.Add(questID);
                progressValues.Add(value);
            }
        }
    }
}
