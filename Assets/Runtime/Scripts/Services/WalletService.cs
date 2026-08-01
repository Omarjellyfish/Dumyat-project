using System;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Plain C# implementation of <see cref="IWalletService"/>, constructed by Reflex.
    /// Reads/writes the wallet save slice through <see cref="ISaveService"/> exactly as
    /// <see cref="PlayerPersistenceService"/> does, and publishes <see cref="MoneyChangedEvent"/>.
    ///
    /// Also owns the "sales pay the player" rule: it self-subscribes to <see cref="SellScoreEvent"/>,
    /// so no merchant code needs to know the wallet exists.
    /// </summary>
    public sealed class WalletService : IWalletService, IDisposable
    {
        private readonly ISaveService _saveService;
        private readonly IEventService _eventService;
        private readonly WalletSaveData _walletData;
        private bool _disposed;

        public WalletService(ISaveService saveService, IEventService eventService)
        {
            _saveService = saveService;
            _eventService = eventService;
            _walletData = _saveService.CurrentSaveData.walletData;

            // Completed sales are the only automatic income source today. SellScoreEvent fires exactly
            // ONCE per accepted deal (FishSaleRecordedEvent fires once per fish, each individually
            // rounded, so crediting on that would drift from the total the merchant actually voiced).
            _eventService?.Subscribe<SellScoreEvent>(HandleSaleCompleted);

            Debug.Log($"WalletService: Initialised -> {Balance} EGP.");
        }

        /// <summary>
        /// Reflex disposes the IDisposable singletons it created when the scope goes away. Without this,
        /// a rebuilt root container would leave the old instance subscribed (EventService's handler lists
        /// are static and survive scene loads) and every sale would credit twice.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _eventService?.Unsubscribe<SellScoreEvent>(HandleSaleCompleted);
        }

        public int Balance => _walletData?.balance ?? 0;

        public bool CanAfford(int amount) => amount >= 0 && Balance >= amount;

        public void Add(int amount, string reason = null)
        {
            if (_walletData == null)
            {
                Debug.LogWarning("WalletService: no wallet save slice; ignoring Add.");
                return;
            }

            if (amount <= 0)
            {
                Debug.LogWarning($"WalletService: Add({amount}) ignored — use TrySpend to debit.");
                return;
            }

            _walletData.balance += amount;
            _saveService.SaveAsync();

            Debug.Log($"WalletService: +{amount} EGP ({reason ?? "unspecified"}) -> {_walletData.balance} EGP.");
            _eventService?.Publish(new MoneyChangedEvent(_walletData.balance, amount, reason));
        }

        public bool TrySpend(int amount, string reason = null)
        {
            if (_walletData == null)
            {
                Debug.LogWarning("WalletService: no wallet save slice; ignoring TrySpend.");
                return false;
            }

            if (amount <= 0)
            {
                Debug.LogWarning($"WalletService: TrySpend({amount}) ignored — amount must be positive.");
                return false;
            }

            if (_walletData.balance < amount)
            {
                // Nothing is mutated. A distinct event so the "money spent" sound does NOT fire.
                _eventService?.Publish(new MoneySpendFailedEvent(amount, _walletData.balance, reason));
                return false;
            }

            _walletData.balance -= amount;
            _saveService.SaveAsync();

            Debug.Log($"WalletService: -{amount} EGP ({reason ?? "unspecified"}) -> {_walletData.balance} EGP.");
            _eventService?.Publish(new MoneyChangedEvent(_walletData.balance, -amount, reason));
            return true;
        }

        // Cached method group so Unsubscribe matches (see CLAUDE.md, Events).
        private void HandleSaleCompleted(SellScoreEvent evt)
        {
            // Total is a sum of already-rounded per-fish prices, so this conversion is exact.
            int paid = Mathf.RoundToInt(evt.Total);
            if (paid <= 0) return;

            Add(paid, $"Fish sale ({evt.FishCount} fish)");
        }
    }
}
