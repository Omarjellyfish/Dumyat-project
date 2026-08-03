using System;

namespace DumyatRuntime
{
    /// <summary>
    /// Save slice holding the player's money. The game only ever deals in whole Egyptian Pounds,
    /// so the balance is an <see cref="int"/> rather than a float — see <see cref="WalletService"/>.
    /// </summary>
    [Serializable]
    public class WalletSaveData
    {
        /// <summary>Player's EGP balance, whole pounds. Never negative.</summary>
        public int balance;
    }
}
