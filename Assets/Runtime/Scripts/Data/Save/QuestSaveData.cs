using System;
using System.Collections.Generic;

namespace DumyatRuntime
{
    [Serializable]
    public class QuestSaveData
    {
        public List<string> activeQuestIDs = new List<string>();
        public List<string> completedQuestIDs = new List<string>();
    }
}
