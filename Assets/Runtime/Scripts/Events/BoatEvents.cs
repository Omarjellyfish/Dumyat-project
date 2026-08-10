namespace DumyatRuntime
{
    /// <summary>
    /// The player has arrived on the fishing boat. Walking is locked from this moment until
    /// <see cref="BoatLeftEvent"/> — the boat is small enough that being able to stroll off the side
    /// is worse than standing still.
    /// </summary>
    public readonly struct BoatBoardedEvent : IGameEvent
    {
    }

    /// <summary>
    /// The player has stepped off the boat and is back ashore, with walking restored.
    /// </summary>
    public readonly struct BoatLeftEvent : IGameEvent
    {
    }
}
