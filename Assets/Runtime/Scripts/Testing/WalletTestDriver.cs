using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// In-Editor dev tool for exercising the wallet without completing a merchant negotiation.
    /// Drop it on any injected scene object, enter Play mode, then right-click the component header
    /// to credit, debit, or attempt an unaffordable spend.
    ///
    /// There is no shop in the game yet, so this is currently the only way to trigger the
    /// "money spent" sound. Not intended to ship on a production GameObject.
    /// </summary>
    public class WalletTestDriver : MonoBehaviour
    {
        [Tooltip("EGP moved by the Add / Spend context-menu commands.")]
        [SerializeField, Min(1)] private int _amount = 250;

        private IWalletService _wallet;

        [Inject]
        private void Inject(IWalletService wallet) => _wallet = wallet;

        [ContextMenu("Wallet/Add EGP")]
        private void DebugAdd()
        {
            if (!EnsureWallet()) return;
            _wallet.Add(_amount, "Debug driver");
        }

        [ContextMenu("Wallet/Spend EGP")]
        private void DebugSpend()
        {
            if (!EnsureWallet()) return;
            bool ok = _wallet.TrySpend(_amount, "Debug driver");
            Debug.Log($"WalletTestDriver: TrySpend({_amount}) -> {ok}. Balance now {_wallet.Balance} EGP.");
        }

        [ContextMenu("Wallet/Spend more than we have")]
        private void DebugSpendUnaffordable()
        {
            if (!EnsureWallet()) return;
            int tooMuch = _wallet.Balance + 999_999;
            bool ok = _wallet.TrySpend(tooMuch, "Debug driver (expected to fail)");
            Debug.Log($"WalletTestDriver: TrySpend({tooMuch}) -> {ok} (expected False). " +
                      $"Balance unchanged at {_wallet.Balance} EGP.");
        }

        [ContextMenu("Wallet/Log balance")]
        private void DebugLogBalance()
        {
            if (!EnsureWallet()) return;
            Debug.Log($"WalletTestDriver: balance is {_wallet.Balance} EGP.");
        }

        private bool EnsureWallet()
        {
            if (_wallet != null) return true;

            Debug.LogWarning($"{nameof(WalletTestDriver)} on '{name}': no IWalletService injected. " +
                             "Enter Play mode first, and check this scene has a ContainerScope.", this);
            return false;
        }
    }
}
