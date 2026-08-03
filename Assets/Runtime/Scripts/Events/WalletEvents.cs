namespace DumyatRuntime
{
    /// <summary>
    /// Published by <see cref="WalletService"/> whenever the player's EGP balance changes.
    /// One event covers both directions: <see cref="Delta"/> is POSITIVE for a top-up (sale payout,
    /// quest reward) and NEGATIVE for a spend, mirroring how <see cref="InventoryChangedEvent"/>
    /// covers both adds and removes. <see cref="NewBalance"/> is authoritative, so a listener that
    /// missed earlier events is still correct.
    /// Never published for a REFUSED spend — see <see cref="MoneySpendFailedEvent"/>.
    /// </summary>
    public readonly struct MoneyChangedEvent : IGameEvent
    {
        /// <summary>Balance AFTER the change, whole Egyptian Pounds.</summary>
        public readonly int NewBalance;

        /// <summary>Signed change in whole EGP. Greater than 0 credited, less than 0 debited. Never 0.</summary>
        public readonly int Delta;

        /// <summary>Optional human-readable cause, for logs and toast text. May be null.</summary>
        public readonly string Reason;

        /// <summary>True when money came IN — drives the "top-up" sound.</summary>
        public bool IsIncome => Delta > 0;

        public MoneyChangedEvent(int newBalance, int delta, string reason = null)
        {
            NewBalance = newBalance;
            Delta = delta;
            Reason = reason;
        }
    }

    /// <summary>
    /// Published by <see cref="WalletService"/> when <see cref="IWalletService.TrySpend"/> is refused
    /// for lack of funds. NOTHING was debited. This exists as its own event so a refusal can flash a
    /// UI or play a rejection sting WITHOUT the "money spent" sound firing — the bool returned by
    /// TrySpend only reaches the direct caller, not decoupled listeners.
    /// </summary>
    public readonly struct MoneySpendFailedEvent : IGameEvent
    {
        /// <summary>What the caller tried to spend, whole Egyptian Pounds.</summary>
        public readonly int Requested;

        /// <summary>The balance at the time of refusal, whole Egyptian Pounds.</summary>
        public readonly int Balance;

        /// <summary>Optional human-readable cause. May be null.</summary>
        public readonly string Reason;

        public MoneySpendFailedEvent(int requested, int balance, string reason = null)
        {
            Requested = requested;
            Balance = balance;
            Reason = reason;
        }
    }
}
