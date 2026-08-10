using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// One product on a shop's shelf: an existing <see cref="ItemData"/> plus what a shop knows about it.
    ///
    /// The price lives here rather than on <see cref="ItemData"/> so the same nails can cost one thing in
    /// the house and another at a dockside stall — the same reason <see cref="ScriptedPriceBook"/> keeps
    /// merchant rates out of the fish assets. Name, icon, description and drop prefab are NOT duplicated;
    /// they are read through <see cref="Item"/>, so an item renamed once is renamed everywhere.
    /// </summary>
    [CreateAssetMenu(fileName = "New Shop Item", menuName = "Dumyat/Shop/Shop Item")]
    public class ShopItemSO : ScriptableObject
    {
        /// <summary>Value of <see cref="StartingStock"/> that means "never runs out".</summary>
        public const int UnlimitedStock = -1;

        [Header("Product")]
        [Tooltip("The inventory item this sells. Supplies the ID, display name, description and icon.")]
        [SerializeField] private ItemData _item;

        [Tooltip("Cost in whole Egyptian Pounds, matching the wallet.")]
        [Min(0)]
        [SerializeField] private int _price = 10;

        [Header("Stock")]
        [Tooltip("How many are on the shelf when the scene loads. Set to -1 for an item that NEVER runs " +
                 "out — staples like nails or glue, which the player should not be able to strand " +
                 "themselves without. Any value of 0 or more counts down as they are bought.")]
        [SerializeField] private int _startingStock = UnlimitedStock;

        [Header("Copy")]
        [Tooltip("Optional shop-specific line. Falls back to the item's own description when left empty, " +
                 "so most products need nothing here.")]
        [TextArea]
        [SerializeField] private string _shopBlurb;

        public ItemData Item => _item;
        public int Price => _price;
        public int StartingStock => _startingStock;

        /// <summary>True when this product never runs out.</summary>
        public bool IsUnlimited => _startingStock < 0;

        /// <summary>Shop name for the product, falling back to the item's own display name.</summary>
        public string DisplayName
        {
            get
            {
                if (_item == null) return name;
                return string.IsNullOrEmpty(_item.DisplayName) ? name : _item.DisplayName;
            }
        }

        /// <summary>Blurb for the detail pane: the shop line if set, otherwise the item's description.</summary>
        public string Description
        {
            get
            {
                if (!string.IsNullOrEmpty(_shopBlurb)) return _shopBlurb;
                return _item != null ? _item.Description : string.Empty;
            }
        }

        /// <summary>Icon for the product well. May be null — the panel then shows its placeholder.</summary>
        public Sprite Icon => _item != null ? _item.Icon : null;

        /// <summary>The inventory ID credited on purchase. Empty when the asset is not finished.</summary>
        public string ItemID => _item != null ? _item.ItemID : string.Empty;

        /// <summary>True when this asset is complete enough to be sold.</summary>
        public bool IsValid => _item != null && !string.IsNullOrEmpty(_item.ItemID);
    }
}
