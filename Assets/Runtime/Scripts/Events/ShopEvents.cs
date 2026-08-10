namespace DumyatRuntime
{
    /// <summary>Why a purchase was refused. Carried on <see cref="PurchaseRefusedEvent"/>.</summary>
    public enum PurchaseRefusal
    {
        /// <summary>The player could not afford it. The wallet also publishes its own failure event.</summary>
        NotEnoughMoney,

        /// <summary>The shelf is empty. Cannot happen for an unlimited product.</summary>
        OutOfStock,

        /// <summary>The <see cref="ShopItemSO"/> has no item assigned, so nothing could be credited.</summary>
        InvalidProduct
    }

    /// <summary>
    /// Published by <see cref="Shop"/> after money has been taken AND the item added to the bag. Both
    /// halves have already succeeded when this fires, so a listener never has to check.
    /// </summary>
    public readonly struct ItemPurchasedEvent : IGameEvent
    {
        public readonly ShopItemSO Product;

        /// <summary>What was actually paid, in whole EGP.</summary>
        public readonly int Price;

        /// <summary>Units left on the shelf afterwards, or -1 when the product is unlimited.</summary>
        public readonly int RemainingStock;

        public ItemPurchasedEvent(ShopItemSO product, int price, int remainingStock)
        {
            Product = product;
            Price = price;
            RemainingStock = remainingStock;
        }
    }

    /// <summary>
    /// Published by <see cref="Shop"/> when a purchase was declined. NOTHING was mutated: no money left
    /// the wallet and no item entered the bag.
    ///
    /// This is separate from <see cref="MoneySpendFailedEvent"/> because that one only covers the
    /// can't-afford case and knows nothing about the shop — an empty shelf never reaches the wallet at all.
    /// </summary>
    public readonly struct PurchaseRefusedEvent : IGameEvent
    {
        public readonly ShopItemSO Product;
        public readonly int Price;
        public readonly PurchaseRefusal Reason;

        public PurchaseRefusedEvent(ShopItemSO product, int price, PurchaseRefusal reason)
        {
            Product = product;
            Price = price;
            Reason = reason;
        }
    }

    /// <summary>
    /// Published by <see cref="Shop"/> whenever its shelf changes, so a panel can redraw without polling.
    /// </summary>
    public readonly struct ShopStockChangedEvent : IGameEvent
    {
        public readonly Shop Shop;

        public ShopStockChangedEvent(Shop shop) => Shop = shop;
    }
}
