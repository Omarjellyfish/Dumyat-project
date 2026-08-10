using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Published when the player takes the rod out of its socket or off the ground. Distinct from the
    /// XRI grab callback it wraps: this one deliberately ignores a socket taking the rod, so it means
    /// "a hand is holding it" rather than "it moved".
    /// </summary>
    public readonly struct FishingRodPickedUpEvent : IGameEvent
    {
        public readonly GameObject Rod;

        public FishingRodPickedUpEvent(GameObject rod)
        {
            Rod = rod;
        }
    }

    /// <summary>
    /// The father has finished his scripted walk and is standing at the jetty. What the player may do
    /// next — board the boat — becomes available here. Published rather than wired directly so the
    /// father does not need to know a boarding board exists.
    /// </summary>
    public readonly struct FatherPathCompletedEvent : IGameEvent
    {
    }

    /// <summary>
    /// The player has pressed and is holding the cast button — the rod is winding up. Published on the
    /// press, not the throw, so a tutorial can teach the two halves of a cast separately.
    /// </summary>
    public readonly struct RodCastArmedEvent : IGameEvent
    {
    }

    /// <summary>
    /// A cast actually left the rod. Published only for a throw hard enough to clear
    /// <c>FishingRodCaster.minimumCastVelocity</c> — a limp swing releases the button but launches
    /// nothing, and reporting that as a cast would teach the player the wrong motion.
    /// </summary>
    public readonly struct RodCastReleasedEvent : IGameEvent
    {
        /// <summary>Forward speed (m/s) of the rod tip at release, before the cast multiplier.</summary>
        public readonly float Speed;

        public RodCastReleasedEvent(float speed)
        {
            Speed = speed;
        }
    }

    /// <summary>
    /// The hook touched water and the line settled — the cast is over and the bait is fishing.
    /// A cast that lands on the boat or the shore never publishes this.
    /// </summary>
    public readonly struct HookLandedInWaterEvent : IGameEvent
    {
        public readonly Vector3 Position;
        /// <summary>Distance (m) from the rod tip to where the hook landed — how far the cast carried.</summary>
        public readonly float Distance;

        public HookLandedInWaterEvent(Vector3 position, float distance)
        {
            Position = position;
            Distance = distance;
        }
    }

    /// <summary>
    /// The player wound the reel by a meaningful amount, in either direction. Published in discrete
    /// chunks by <see cref="ReelTurnReporter"/> rather than continuously — the reel's rotation is a
    /// per-frame value, and putting that on the bus would flood it.
    /// </summary>
    public readonly struct ReelTurnedEvent : IGameEvent
    {
        /// <summary>Degrees turned since the last report. Always positive; direction is not reported.</summary>
        public readonly float Degrees;

        public ReelTurnedEvent(float degrees)
        {
            Degrees = degrees;
        }
    }

    /// <summary>
    /// The player got a hand on a fish. Note this is the grab, not the catch — the catch is
    /// <see cref="FishCaughtEvent"/>, published the moment the hook takes hold, well before the player
    /// has hauled the fish in and taken it off the line.
    /// </summary>
    public readonly struct FishGrabbedEvent : IGameEvent
    {
        public readonly Fish Fish;

        public FishGrabbedEvent(Fish fish)
        {
            Fish = fish;
        }
    }

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
