namespace DumyatRuntime
{
    /// <summary>
    /// Published by <see cref="Fish.OnCaught"/> when a fish is successfully caught.
    /// Consumed by <see cref="QuestService"/> (and any future fishing-stats tracker).
    /// </summary>
    public readonly struct FishCaughtEvent : IGameEvent
    {
        public readonly string FishName;
        public readonly float Weight;
        public readonly float Value;

        public FishCaughtEvent(string fishName, float weight, float value)
        {
            FishName = fishName;
            Weight = weight;
            Value = value;
        }
    }
}
