namespace DumyatRuntime
{
    /// <summary>
    /// Implemented by any ScriptableObject that can be dragged into a quest's target slot.
    /// The <see cref="QuestService"/> reads <see cref="TargetID"/> to match against
    /// incoming gameplay events.
    /// </summary>
    public interface IQuestTarget
    {
        /// <summary>The stable string identifier used for event matching (e.g. an ItemID or RecipeName).</summary>
        string TargetID { get; }
    }
}
