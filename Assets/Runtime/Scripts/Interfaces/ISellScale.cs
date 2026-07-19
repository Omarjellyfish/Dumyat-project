using System.Collections.Generic;

namespace DumyatRuntime
{
    /// <summary>
    /// A merchant's physical sell scale: the source of truth for WHICH fish are being offered for
    /// sale. A fish counts only once it is physically resting on the scale (released from the hand
    /// inside the scale's trigger volume). The merchant reads this to know the catch — it can neither
    /// see nor buy a fish that is not on the scale.
    ///
    /// Backed by <see cref="FishSellScale"/>. Consumers should re-read it at decision points (it is a
    /// live view) and may react to <see cref="ScaleContentsChangedEvent"/> on the event bus.
    /// </summary>
    public interface ISellScale
    {
        /// <summary>The world fish currently resting on the scale (live; excludes held/destroyed fish).</summary>
        IReadOnlyList<Fish> FishOnScale { get; }

        /// <summary>
        /// The resting fish as merchant <see cref="FishData"/> (each via <c>Fish.ToFishData()</c>, so
        /// references are stable to the sale). A fresh snapshot — safe for the caller to keep/iterate.
        /// </summary>
        IReadOnlyList<FishData> FishDataOnScale { get; }

        /// <summary>True while at least one fish is resting on the scale.</summary>
        bool HasFish { get; }
    }
}
