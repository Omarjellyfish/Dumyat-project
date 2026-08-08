namespace DumyatRuntime
{
    /// <summary>
    /// Published by <see cref="ShipAssemblyManager"/> each time a part seats in one of the ship's
    /// sockets. Carries the running count so a listener can say "3 of 5" without holding a reference
    /// to the manager.
    /// </summary>
    public readonly struct ShipPartSocketedEvent : IGameEvent
    {
        public readonly string SocketId;
        public readonly int FilledCount;
        public readonly int RequiredCount;

        public ShipPartSocketedEvent(string socketId, int filledCount, int requiredCount)
        {
            SocketId = socketId;
            FilledCount = filledCount;
            RequiredCount = requiredCount;
        }
    }

    /// <summary>
    /// Published once, when the last required socket fills. The EventBus twin of
    /// <c>ShipAssemblyManager.OnShipFullyAssembled</c>, which stays for scene objects that hold a
    /// direct reference to the manager — this exists so systems that should not know the manager at
    /// all (the tutorial, quests) can react too.
    /// </summary>
    public readonly struct ShipAssembledEvent : IGameEvent
    {
        public readonly int PartCount;

        public ShipAssembledEvent(int partCount)
        {
            PartCount = partCount;
        }
    }
}
