using System.Collections.Generic;

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

        /// <summary>
        /// Every species this inventory can produce, with its weight band — the market's catalog. Given
        /// to the merchant so it only acknowledges real fish and can reject out-of-band weights, rather
        /// than assuming the species itself. May be null/empty when a catalog isn't known.
        /// </summary>
        IReadOnlyList<FishCatalogEntry> Catalog { get; }

        /// <summary>
        /// Called by the negotiation flow when a fish is SOLD, so the inventory can remove/consume that
        /// exact fish (the sell-process side of inventory validation). Implementations that don't hold a
        /// persistent stock (e.g. the random test provider) may treat this as a no-op.
        /// </summary>
        void MarkFishSold(FishData fish);
    }
}
