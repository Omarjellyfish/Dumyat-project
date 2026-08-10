namespace DumyatRuntime
{
    /// <summary>
    /// Published by <see cref="HoopScoreZone"/> the moment a ball passes DOWNWARD through the rim.
    /// Carries no count: the zone knows only that a basket happened, and <see cref="HoopScoreTracker"/>
    /// owns the running total. Keeping the two apart means a second hoop can be dropped into a scene
    /// without either of them changing.
    /// </summary>
    public readonly struct HoopScoredEvent : IGameEvent
    {
        /// <summary>Where the ball crossed the rim plane, for positional audio and popups.</summary>
        public readonly UnityEngine.Vector3 Position;

        /// <summary>The hoop that was scored on, so a scene with several can tell them apart.</summary>
        public readonly HoopScoreZone Hoop;

        public HoopScoredEvent(UnityEngine.Vector3 position, HoopScoreZone hoop)
        {
            Position = position;
            Hoop = hoop;
        }
    }

    /// <summary>
    /// Published by <see cref="HoopScoreTracker"/> after it has counted a basket. <see cref="Total"/> is
    /// authoritative, so a listener that missed earlier events is still correct — the same contract
    /// <see cref="MoneyChangedEvent"/> keeps for the wallet.
    ///
    /// The count is SESSION-ONLY and deliberately not saved: this is an easter egg, and a lifetime
    /// figure would imply a stat screen that does not exist. It resets whenever the scene reloads.
    /// </summary>
    public readonly struct HoopScoreChangedEvent : IGameEvent
    {
        /// <summary>Baskets scored since this scene loaded. Always 1 or more when published.</summary>
        public readonly int Total;

        /// <summary>Where the basket was scored, carried through from <see cref="HoopScoredEvent"/>.</summary>
        public readonly UnityEngine.Vector3 Position;

        public HoopScoreChangedEvent(int total, UnityEngine.Vector3 position)
        {
            Total = total;
            Position = position;
        }
    }
}
