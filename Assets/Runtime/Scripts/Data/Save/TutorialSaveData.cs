using System;
using System.Collections.Generic;

namespace DumyatRuntime
{
    /// <summary>
    /// Persisted tutorial completion state. Stored inside <see cref="GameSaveData"/>.
    /// Each entry is the <see cref="TutorialSequenceSO.SequenceId"/> of a completed tutorial.
    /// </summary>
    [Serializable]
    public class TutorialSaveData
    {
        public List<string> CompletedSequenceIds = new List<string>();
    }
}
