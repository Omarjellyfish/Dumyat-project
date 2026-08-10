using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The shelf as an asset: every <see cref="ShopItemSO"/> a shop stocks, in display order. The same
    /// shape as <see cref="ItemDatabase"/>, and for the same reason — the catalogue is content, so it
    /// belongs in a file a designer can edit and reuse, not in a serialized array on one scene object.
    ///
    /// This is what lets two scenes share a shelf, or one scene hold two shops with different stock:
    /// a <see cref="Shop"/> points at a database rather than owning the list. Prices and stock stay on
    /// the individual <see cref="ShopItemSO"/> assets; this only decides what is on sale and in what order.
    /// </summary>
    [CreateAssetMenu(fileName = "ShopDatabase", menuName = "Dumyat/Shop/Database")]
    public class ShopDatabase : ScriptableObject
    {
        [Tooltip("Everything this shop sells, in the order the panel lists it.")]
        [SerializeField] private List<ShopItemSO> _allProducts = new List<ShopItemSO>();

        public IReadOnlyList<ShopItemSO> AllProducts => _allProducts;

        /// <summary>How many products are on the shelf, including any unfinished entries.</summary>
        public int Count => _allProducts != null ? _allProducts.Count : 0;

        /// <summary>
        /// The product at <paramref name="index"/>, or null when the index is out of range or the slot
        /// is empty. Null-safe rather than throwing, because a half-filled list is a normal state while
        /// a shop is being authored.
        /// </summary>
        public ShopItemSO ProductAt(int index)
        {
            if (_allProducts == null || index < 0 || index >= _allProducts.Count) return null;
            return _allProducts[index];
        }

        /// <summary>
        /// The product selling the item with <paramref name="itemID"/>, or null when this shop does not
        /// stock it. Linear scan like <see cref="ItemDatabase.GetItemByID"/> — fine for a shelf read once
        /// when a panel opens, but do not call it per frame.
        /// </summary>
        public ShopItemSO GetProductByItemID(string itemID)
        {
            if (string.IsNullOrEmpty(itemID) || _allProducts == null) return null;

            foreach (ShopItemSO product in _allProducts)
            {
                // Unfinished assets are skipped rather than dereferenced — ItemDatabase throws on these.
                if (product != null && product.ItemID == itemID) return product;
            }

            Debug.LogWarning($"{nameof(ShopDatabase)} '{name}': no product sells item ID '{itemID}'.", this);
            return null;
        }
    }
}
