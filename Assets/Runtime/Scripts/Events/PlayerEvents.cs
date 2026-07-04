namespace DumyatRuntime
{
    /// <summary>Published by <see cref="PlayerPersistenceService"/> when a persistence key is unlocked for the first time.</summary>
    public readonly struct KeyUnlockedEvent : IGameEvent
    {
        public readonly string Key;

        public KeyUnlockedEvent(string key)
        {
            Key = key;
        }
    }
}
