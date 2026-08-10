using System.Collections.Generic;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// A shelf of <see cref="ShopItemSO"/> products and the one rule that matters: take the money, then
    /// hand over the goods. Owns no UI — <see cref="ShopToolkitUI"/> draws it, and anything else that
    /// wants to react listens for <see cref="ItemPurchasedEvent"/>.
    ///
    /// Runtime stock is held HERE and not on the ScriptableObject. Counting down a field on the asset
    /// would dirty it in the Editor, persist into the build, and leave the shop permanently empty after
    /// one play session — a classic and very confusing bug. <see cref="ShopItemSO.StartingStock"/> stays
    /// the starting figure; this class holds what is left.
    /// </summary>
    public class Shop : MonoBehaviour
    {
        [Tooltip("Everything this shop sells, in the order it should be listed.")]
        [SerializeField] private ShopItemSO[] _catalogue;

        [Tooltip("Shown as the small label above the SHOP title. Leave empty to hide it.")]
        [SerializeField] private string _shopName = "EL DUKKAN";

        private IWalletService _wallet;
        private IInventoryService _inventory;
        private IEventService _events;

        // Parallel to _catalogue: units left, or ShopItemSO.UnlimitedStock for a product that never runs out.
        private int[] _remaining;

        /// <summary>Products on the shelf, in display order. Never null.</summary>
        public IReadOnlyList<ShopItemSO> Catalogue => _catalogue ?? System.Array.Empty<ShopItemSO>();

        /// <summary>Name shown above the panel title. May be empty.</summary>
        public string ShopName => _shopName;

        [Inject]
        private void Inject(IWalletService wallet, IInventoryService inventory, IEventService events)
        {
            _wallet = wallet;
            _inventory = inventory;
            _events = events;
        }

        private void Awake() => ResetStock();

        /// <summary>Restores every shelf to its starting count. Called on Awake, so stock is per-session.</summary>
        public void ResetStock()
        {
            int count = _catalogue != null ? _catalogue.Length : 0;
            _remaining = new int[count];
            for (int i = 0; i < count; i++)
                _remaining[i] = _catalogue[i] != null ? _catalogue[i].StartingStock : 0;
        }

        /// <summary>Units left for <paramref name="index"/>, or -1 when unlimited. -1 also for a bad index.</summary>
        public int RemainingStock(int index)
        {
            if (_remaining == null || index < 0 || index >= _remaining.Length) return ShopItemSO.UnlimitedStock;
            return _remaining[index];
        }

        /// <summary>True when <paramref name="index"/> can still be bought at all, money aside.</summary>
        public bool IsInStock(int index)
        {
            int left = RemainingStock(index);
            return left < 0 || left > 0;
        }

        /// <summary>True when the player can afford the product at <paramref name="index"/> right now.</summary>
        public bool CanAfford(int index)
        {
            ShopItemSO product = ProductAt(index);
            if (product == null || _wallet == null) return false;
            return _wallet.CanAfford(product.Price);
        }

        /// <summary>
        /// How much more the player needs for <paramref name="index"/>; 0 when they can already afford it.
        /// Drives the "SHORT 75" button without the UI doing arithmetic on the wallet.
        /// </summary>
        public int ShortfallFor(int index)
        {
            ShopItemSO product = ProductAt(index);
            if (product == null || _wallet == null) return 0;
            return Mathf.Max(0, product.Price - _wallet.Balance);
        }

        /// <summary>The product at <paramref name="index"/>, or null when the index or asset is bad.</summary>
        public ShopItemSO ProductAt(int index)
        {
            if (_catalogue == null || index < 0 || index >= _catalogue.Length) return null;
            return _catalogue[index];
        }

        /// <summary>
        /// Buys one unit of <paramref name="index"/>. Returns true only when money was taken AND the item
        /// reached the bag. On any refusal nothing is mutated and <see cref="PurchaseRefusedEvent"/> is
        /// published instead.
        /// </summary>
        public bool TryBuy(int index)
        {
            ShopItemSO product = ProductAt(index);
            if (product == null || !product.IsValid)
            {
                Debug.LogWarning($"{nameof(Shop)} on '{name}': catalogue slot {index} has no usable ItemData, " +
                                 "so there is nothing to credit. Assign one on the ShopItemSO asset.", this);
                Refuse(product, PurchaseRefusal.InvalidProduct);
                return false;
            }

            if (!IsInStock(index))
            {
                Refuse(product, PurchaseRefusal.OutOfStock);
                return false;
            }

            if (_wallet == null || _inventory == null)
            {
                Debug.LogWarning($"{nameof(Shop)} on '{name}': wallet or inventory was not injected — the " +
                                 "purchase cannot go through. Does this scene have a ContainerScope?", this);
                return false;
            }

            // Money first. TrySpend is atomic: on refusal the balance is untouched and it publishes
            // MoneySpendFailedEvent itself, so the item must not be handed over.
            if (!_wallet.TrySpend(product.Price, $"Bought {product.DisplayName}"))
            {
                Refuse(product, PurchaseRefusal.NotEnoughMoney);
                return false;
            }

            _inventory.AddItemToInventory(product.ItemID, 1);

            // Only a limited shelf counts down; an unlimited one stays at -1 forever.
            if (_remaining[index] > 0) _remaining[index]--;

            _events?.Publish(new ItemPurchasedEvent(product, product.Price, _remaining[index]));
            _events?.Publish(new ShopStockChangedEvent(this));
            return true;
        }

        private void Refuse(ShopItemSO product, PurchaseRefusal reason) =>
            _events?.Publish(new PurchaseRefusedEvent(product, product != null ? product.Price : 0, reason));
    }
}
