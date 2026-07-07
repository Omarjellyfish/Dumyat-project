namespace DumyatRuntime
{
    /// <summary>
    /// Supplies the fish the player is currently trying to sell to a merchant. This is the seam
    /// between the negotiation flow and whatever holds the player's catch: the real inventory can
    /// implement it later, and the test provides a randomized implementation now. Keeps
    /// <see cref="NegotiationFlowController"/> decoupled from any concrete inventory.
    /// </summary>
    public interface IFishInventory
    {
        /// <summary>True when there is at least one fish available to sell.</summary>
        bool HasFish { get; }

        /// <summary>
        /// Provides the next fish to sell. Returns false (and a null <paramref name="fish"/>) when
        /// none is available. Implementations decide whether this consumes the fish or peeks.
        /// </summary>
        bool TryGetFishToSell(out FishData fish);
    }
}
