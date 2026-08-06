namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for the player's money. The game only ever deals in whole Egyptian Pounds
    /// (see <c>MerchantNegotiationLLM</c>), so the balance is an <see cref="int"/> throughout.
    ///
    /// Deliberately separate from <see cref="IInventoryService"/>: money is not an item. Modelling it
    /// as an <c>InventoryItem</c> would leak into every consumer that walks <c>CurrentItems</c> — the
    /// inventory panel would render a cash row with a Drop button, and <c>ItemSpawner</c> would try to
    /// instantiate a drop prefab for it.
    ///
    /// Implemented by <see cref="WalletService"/>, registered in <c>GameInstaller</c> (root scope).
    /// </summary>
    public interface IWalletService
    {
        /// <summary>Current balance in whole EGP. Never negative.</summary>
        int Balance { get; }

        /// <summary>True when <paramref name="amount"/> could be spent right now.</summary>
        bool CanAfford(int amount);

        /// <summary>
        /// Credits <paramref name="amount"/> EGP, persists, and publishes <see cref="MoneyChangedEvent"/>
        /// with a positive Delta. Amounts of zero or less are ignored with a warning — use
        /// <see cref="TrySpend"/> to take money out.
        /// </summary>
        /// <param name="reason">Optional human-readable cause, carried on the event and into logs.</param>
        void Add(int amount, string reason = null);

        /// <summary>
        /// Debits <paramref name="amount"/> EGP if affordable: persists, publishes
        /// <see cref="MoneyChangedEvent"/> with a negative Delta, and returns true.
        /// On failure NOTHING is mutated, <see cref="MoneySpendFailedEvent"/> is published instead,
        /// and it returns false.
        /// </summary>
        /// <param name="reason">Optional human-readable cause, carried on the event and into logs.</param>
        bool TrySpend(int amount, string reason = null);
    }
}
