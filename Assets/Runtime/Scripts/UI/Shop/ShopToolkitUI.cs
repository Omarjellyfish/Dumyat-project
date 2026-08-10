using System.Collections.Generic;
using System.Globalization;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Draws a <see cref="Shop"/> as the counter-layout panel: the selected product held large on the
    /// left, the rest of the shelf as a price list on the right. Owns no purchase logic — every BUY goes
    /// through <see cref="Shop.TryBuy"/>, which is the only place money and stock move.
    ///
    /// Follows the same shape as <see cref="InventoryToolkitUI"/>, including the two things that panel
    /// documents the hard way: <see cref="BindDocument"/> is split out so it can re-run when the
    /// UIDocument rebuilds its tree, and open/closed is tracked in a field rather than read back off
    /// <c>style.display</c>, because a never-assigned inline style does not compare cleanly against
    /// <see cref="DisplayStyle"/>.
    /// </summary>
    public class ShopToolkitUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private UIDocument _uiDocument;

        [Tooltip("The shelf this panel sells from. Falls back to one on this GameObject or its parents.")]
        [SerializeField] private Shop _shop;

        [Tooltip("Row template (ShopRow.uxml). Required — without it the list cannot be built.")]
        [SerializeField] private VisualTreeAsset _rowTemplate;

        [Header("Settings")]
        [SerializeField] private bool _startOpen = true;

        private IEventService _events;
        private IWalletService _wallet;

        private VisualElement _root;
        private VisualElement _walletChip;
        private Label _walletBalance;
        private Label _shopKicker;
        private VisualElement _pickerList;
        private Label _emptyHint;

        // Focus pane
        private VisualElement _focusIcon;
        private VisualElement _focusIconEmpty;
        private Label _focusName;
        private Label _focusStock;
        private Label _focusDescription;
        private Label _focusPriceValue;
        private Button _buyButton;

        private readonly List<VisualElement> _rows = new List<VisualElement>();
        private int _selectedIndex;
        private bool _isOpen;

        [Inject]
        private void Inject(IEventService events, IWalletService wallet)
        {
            _events = events;
            _wallet = wallet;
        }

        private void OnEnable()
        {
            if (_uiDocument == null) _uiDocument = GetComponent<UIDocument>();
            if (_shop == null) _shop = GetComponentInParent<Shop>();

            if (_uiDocument == null)
            {
                Debug.LogWarning($"{nameof(ShopToolkitUI)} on '{name}': no UIDocument — nothing to draw into.", this);
                return;
            }
            if (_shop == null)
            {
                Debug.LogWarning($"{nameof(ShopToolkitUI)} on '{name}': no Shop assigned or found in parents — " +
                                 "the panel has no shelf to sell from.", this);
                return;
            }

            BindDocument();

            if (_events == null)
            {
                Debug.LogWarning($"{nameof(ShopToolkitUI)} on '{name}': no IEventService injected — the panel " +
                                 "will not follow the wallet or the shelf. Does this scene have a ContainerScope?", this);
                return;
            }

            _events.Subscribe<MoneyChangedEvent>(HandleMoneyChanged);
            _events.Subscribe<ShopStockChangedEvent>(HandleStockChanged);

            _isOpen = _startOpen;
            _root.style.display = _startOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (_startOpen) RefreshAll();
        }

        private void OnDisable()
        {
            if (_events == null) return;
            _events.Unsubscribe<MoneyChangedEvent>(HandleMoneyChanged);
            _events.Unsubscribe<ShopStockChangedEvent>(HandleStockChanged);
        }

        /// <summary>Caches the elements this panel drives. Re-runnable — see <see cref="EnsureBound"/>.</summary>
        private void BindDocument()
        {
            _root = _uiDocument.rootVisualElement;
            if (_root == null) return;

            _walletChip = _root.Q<VisualElement>("WalletChip");
            _walletBalance = _root.Q<Label>("WalletBalance");
            _shopKicker = _root.Q<Label>("ShopKicker");
            _pickerList = _root.Q<ScrollView>("PickerList");
            _emptyHint = _root.Q<Label>("EmptyHint");

            _focusIcon = _root.Q<VisualElement>("FocusIcon");
            _focusIconEmpty = _root.Q<VisualElement>("FocusIconEmpty");
            _focusName = _root.Q<Label>("FocusName");
            _focusStock = _root.Q<Label>("FocusStock");
            _focusDescription = _root.Q<Label>("FocusDescription");
            _focusPriceValue = _root.Q<Label>("FocusPriceValue");

            _buyButton = _root.Q<Button>("BuyButton");
            if (_buyButton != null) _buyButton.clicked += HandleBuyClicked;

            // Rows belong to the tree that just went away; keeping them would leave the panel updating
            // orphans and skipping the rebuild it thinks it has already done.
            _rows.Clear();

            // A fresh tree opens with default styles, so the panel would silently re-open on a rebuild.
            _root.style.display = _isOpen ? DisplayStyle.Flex : DisplayStyle.None;

            if (_shopKicker != null)
            {
                bool hasName = _shop != null && !string.IsNullOrEmpty(_shop.ShopName);
                _shopKicker.text = hasName ? _shop.ShopName : string.Empty;
                _shopKicker.style.display = hasName ? DisplayStyle.Flex : DisplayStyle.None;
            }

            SetBalanceText(_wallet?.Balance ?? 0);
        }

        /// <summary>
        /// Re-resolves the visual tree when the cached one has gone stale. <c>panel == null</c> is the
        /// test because that is exactly what "detached from anything being drawn" means — see the long
        /// note on <see cref="InventoryToolkitUI"/>, which hit this after a scene transition.
        /// </summary>
        private bool EnsureBound()
        {
            if (_uiDocument == null) return false;
            if (_root != null && _root.panel != null) return true;

            BindDocument();
            return _root != null && _root.panel != null;
        }

        public void ToggleUI()
        {
            if (_isOpen) CloseUI();
            else OpenUI();
        }

        public void OpenUI()
        {
            if (!EnsureBound() || _isOpen) return;
            _isOpen = true;
            _root.style.display = DisplayStyle.Flex;
            RefreshAll();
        }

        public void CloseUI()
        {
            if (_root == null || !_isOpen) return;
            _isOpen = false;
            _root.style.display = DisplayStyle.None;
        }

        // ------------------------------------------------------------------ events --

        // Deliberately does NOT early-return when hidden: writing a label costs nothing and guarantees
        // the balance is right the instant the panel opens.
        private void HandleMoneyChanged(MoneyChangedEvent evt)
        {
            SetBalanceText(evt.NewBalance);

            // Affordability changed, so SHORT buttons and coral prices may need to flip back.
            if (_isOpen && EnsureBound())
            {
                RefreshRowAffordability();
                RefreshFocus();
            }

            if (!_isOpen || _walletChip == null) return;
            FlashWalletChip(evt.IsIncome);
        }

        private void HandleStockChanged(ShopStockChangedEvent evt)
        {
            if (evt.Shop != _shop) return;   // a scene may hold more than one shelf
            if (!_isOpen || !EnsureBound()) return;
            RefreshRows();
            RefreshFocus();
        }

        private void HandleBuyClicked()
        {
            if (_shop == null) return;
            _shop.TryBuy(_selectedIndex);
            // Success publishes ShopStockChangedEvent and MoneyChangedEvent, which redraw the panel.
            // A refusal changes nothing, so the panel is already correct — but the focus pane is
            // refreshed anyway so a shelf that emptied elsewhere cannot leave a stale BUY on screen.
            RefreshFocus();
        }

        // ------------------------------------------------------------------- render --

        private void RefreshAll()
        {
            SetBalanceText(_wallet?.Balance ?? 0);
            RefreshRows();
            RefreshFocus();
        }

        private void RefreshRows()
        {
            if (_pickerList == null || _shop == null) return;

            ScrollView scroll = _pickerList as ScrollView;
            VisualElement container = scroll != null ? scroll.contentContainer : _pickerList;

            container.Clear();
            _rows.Clear();

            IReadOnlyList<ShopItemSO> catalogue = _shop.Catalogue;

            if (_emptyHint != null)
                _emptyHint.style.display = catalogue.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;

            if (_rowTemplate == null)
            {
                Debug.LogWarning($"{nameof(ShopToolkitUI)} on '{name}': no row template assigned " +
                                 "(ShopRow.uxml) — the price list cannot be built.", this);
                return;
            }

            for (int i = 0; i < catalogue.Count; i++)
            {
                ShopItemSO product = catalogue[i];
                if (product == null) continue;

                VisualElement row = _rowTemplate.Instantiate();
                // Instantiate() returns a wrapper; the styled row is the first child.
                VisualElement styled = row.childCount > 0 ? row[0] : row;

                Label nameLabel = styled.Q<Label>("RowName");
                if (nameLabel != null) nameLabel.text = product.DisplayName;

                Label priceLabel = styled.Q<Label>("RowPriceValue");
                if (priceLabel != null) priceLabel.text = product.Price.ToString("N0", CultureInfo.InvariantCulture);

                ApplyIcon(styled.Q<VisualElement>("RowIcon"),
                          styled.Q<Label>("RowIconPlaceholder"),
                          product.Icon);

                int index = i;   // captured per row, not the loop variable
                styled.RegisterCallback<ClickEvent>(_ => Select(index));

                container.Add(row);
                _rows.Add(styled);
            }

            if (_selectedIndex >= _rows.Count) _selectedIndex = 0;
            RefreshRowStates();
        }

        private void RefreshRowStates()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                VisualElement row = _rows[i];
                row.EnableInClassList("pick--selected", i == _selectedIndex);
                row.EnableInClassList("pick--out", !_shop.IsInStock(i));

                Label price = row.Q<Label>("RowPriceValue");
                if (price == null) continue;

                bool outOfStock = !_shop.IsInStock(i);
                bool tooDear = !outOfStock && !_shop.CanAfford(i);
                price.EnableInClassList("pick-price-num--out", outOfStock);
                price.EnableInClassList("pick-price-num--short", tooDear);
            }
        }

        private void RefreshRowAffordability() => RefreshRowStates();

        private void Select(int index)
        {
            _selectedIndex = index;
            RefreshRowStates();
            RefreshFocus();
        }

        private void RefreshFocus()
        {
            if (_shop == null) return;

            ShopItemSO product = _shop.ProductAt(_selectedIndex);
            if (product == null)
            {
                if (_focusName != null) _focusName.text = "—";
                if (_focusStock != null) _focusStock.text = string.Empty;
                if (_focusDescription != null) _focusDescription.text = string.Empty;
                if (_focusPriceValue != null) _focusPriceValue.text = "0";
                if (_buyButton != null) _buyButton.SetEnabled(false);
                return;
            }

            if (_focusName != null) _focusName.text = product.DisplayName;
            if (_focusDescription != null) _focusDescription.text = product.Description;
            if (_focusPriceValue != null)
                _focusPriceValue.text = product.Price.ToString("N0", CultureInfo.InvariantCulture);

            ApplyIcon(_focusIcon, _focusIconEmpty, product.Icon);

            int remaining = _shop.RemainingStock(_selectedIndex);
            bool inStock = _shop.IsInStock(_selectedIndex);
            bool affordable = _shop.CanAfford(_selectedIndex);

            if (_focusStock != null)
            {
                _focusStock.text = StockLine(remaining);
                _focusStock.EnableInClassList("focus-stock--last", inStock && remaining == 1);
                _focusStock.EnableInClassList("focus-stock--none", !inStock);
            }

            if (_focusPriceValue != null)
                _focusPriceValue.EnableInClassList("price-lg-value--short", inStock && !affordable);

            if (_buyButton == null) return;

            _buyButton.EnableInClassList("buy-btn--out", !inStock);
            _buyButton.EnableInClassList("buy-btn--short", inStock && !affordable);

            if (!inStock) _buyButton.text = "SOLD OUT";
            else if (!affordable) _buyButton.text = $"SHORT {_shop.ShortfallFor(_selectedIndex)}";
            else _buyButton.text = "BUY";

            // Kept clickable only when it would actually succeed, so a press can never half-complete.
            _buyButton.SetEnabled(inStock && affordable);
        }

        /// <summary>Wording for the stock line. An unlimited product says nothing about counts.</summary>
        private static string StockLine(int remaining)
        {
            if (remaining < 0) return "Always in stock";
            if (remaining == 0) return "Out of stock";
            if (remaining == 1) return "Last one on the shelf";
            return $"{remaining} in stock";
        }

        /// <summary>
        /// Fills an icon well from a sprite, hiding the placeholder mark. A missing sprite leaves the
        /// tinted well and its glyph, so a half-built catalogue still looks deliberate.
        /// </summary>
        private static void ApplyIcon(VisualElement well, VisualElement placeholder, Sprite icon)
        {
            if (well == null) return;

            well.style.backgroundImage = icon != null ? new StyleBackground(icon) : new StyleBackground();
            if (placeholder != null)
                placeholder.style.display = icon != null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void SetBalanceText(int balance)
        {
            if (_walletBalance == null) return;
            _walletBalance.text = balance.ToString("N0", CultureInfo.InvariantCulture);
        }

        private void FlashWalletChip(bool isIncome)
        {
            const string gain = "wallet-chip--gain";
            const string loss = "wallet-chip--loss";

            _walletChip.RemoveFromClassList(gain);
            _walletChip.RemoveFromClassList(loss);
            _walletChip.AddToClassList(isIncome ? gain : loss);

            // UI Toolkit's own scheduler — no coroutine, and it survives the panel being hidden mid-flash.
            _walletChip.schedule.Execute(() =>
            {
                _walletChip.RemoveFromClassList(gain);
                _walletChip.RemoveFromClassList(loss);
            }).ExecuteLater(280);
        }
    }
}
