namespace DumyatRuntime
{
    /// <summary>
    /// Published by <see cref="Fish.OnCaught"/> the moment a fish is hooked. Lets the player's fish
    /// inventory take ownership of the catch without the fishing system knowing the inventory exists
    /// — the first half of the catch → sell loop that <see cref="FishSaleRecordedEvent"/> closes.
    /// Also consumed by <see cref="QuestService"/> for <see cref="QuestTriggerType.FishCaught"/>
    /// quests, which read the species off the carried <see cref="Fish"/>.
    /// </summary>
    public readonly struct FishCaughtEvent : IGameEvent
    {
        /// <summary>The fish that was just caught. Its species/weight/value are already decided.</summary>
        public readonly Fish Fish;

        public FishCaughtEvent(Fish fish)
        {
            Fish = fish;
        }
    }
}
