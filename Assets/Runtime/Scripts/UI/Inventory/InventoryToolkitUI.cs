using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public class InventoryToolkitUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private ItemDatabase _itemDatabase;
        [Tooltip("Optional. If empty, a code-generated fallback slot will be used.")]
        [SerializeField] private VisualTreeAsset _slotTemplate;

        [Header("Settings")]
        [SerializeField] private bool _startOpen = false;

        private IInventoryService _inventory;
        private IEventService _eventService;
        private IWalletService _wallet;

        [Inject]
        private void Inject(IInventoryService inventory, IEventService eventService, IWalletService wallet)
        {
            _inventory = inventory;
            _eventService = eventService;
            _wallet = wallet;
        }

        private VisualElement _root;
        private VisualElement _slotContainer;
        private VisualElement _walletChip;
        private Label _walletLabel;
        private Label _emptyHint;
        private Dictionary<string, VisualElement> _activeSlots = new Dictionary<string, VisualElement>();

        // Open/closed is tracked here rather than read back off _root.style.display: an inline style
        // that has never been assigned does not compare cleanly against DisplayStyle, so a panel whose
        // OnEnable bailed early would read as already-open and refuse to open at all.
        private bool _isOpen;

        private void OnEnable()
        {
            if (_uiDocument == null)
                _uiDocument = GetComponent<UIDocument>();

            if (_uiDocument == null) return;

            _root = _uiDocument.rootVisualElement;

            // The main container in your UI Builder document where slots will be added
            _slotContainer = _root.Q<VisualElement>("SlotContainer");
            _walletChip = _root.Q<VisualElement>("WalletChip");
            _walletLabel = _root.Q<Label>("WalletBalance");
            _emptyHint = _root.Q<Label>("EmptyHint");

            // Seed from the service so the number is right before the first MoneyChangedEvent arrives.
            SetBalanceText(_wallet?.Balance ?? 0);

            if (_eventService == null) return;

            _eventService.Subscribe<InventoryChangedEvent>(HandleInventoryChanged);
            _eventService.Subscribe<MoneyChangedEvent>(HandleMoneyChanged);

            _isOpen = _startOpen;
            _root.style.display = _startOpen ? DisplayStyle.Flex : DisplayStyle.None;

            if (_startOpen)
            {
                RefreshFullUI();
            }
        }

        private void OnDisable()
        {
            if (_eventService == null) return;

            _eventService.Unsubscribe<InventoryChangedEvent>(HandleInventoryChanged);
            _eventService.Unsubscribe<MoneyChangedEvent>(HandleMoneyChanged);
        }

        public void ToggleUI()
        {
            if (_root == null) return;

            if (_isOpen) CloseUI();
            else OpenUI();
        }

        public void OpenUI()
        {
            if (_root == null || _isOpen) return;

            _isOpen = true;
            _root.style.display = DisplayStyle.Flex;
            RefreshFullUI();
            _eventService?.Publish(new InventoryPanelToggledEvent(true));
        }

        public void CloseUI()
        {
            if (_root == null || !_isOpen) return;

            _isOpen = false;
            _root.style.display = DisplayStyle.None;
            _eventService?.Publish(new InventoryPanelToggledEvent(false));
        }

        private void HandleInventoryChanged(InventoryChangedEvent evt)
        {
            InventoryItem updatedItem = evt.Item;

            if (_root == null || _root.style.display == DisplayStyle.None) return;

            if (updatedItem.Quantity <= 0)
            {
                RemoveSlot(updatedItem.ItemID);
            }
            else
            {
                UpdateOrSpawnSlot(updatedItem);
            }
        }

        // Deliberately does NOT early-return when the panel is hidden, unlike HandleInventoryChanged:
        // writing a label costs nothing and it guarantees the balance is correct the instant the bag opens.
        private void HandleMoneyChanged(MoneyChangedEvent evt)
        {
            SetBalanceText(evt.NewBalance);

            // Only animate when the panel is actually on screen.
            if (_walletChip == null || _root == null || _root.style.display == DisplayStyle.None) return;

            FlashWalletChip(evt.IsIncome);
        }

        private void SetBalanceText(int balance)
        {
            if (_walletLabel == null) return;
            _walletLabel.text = balance.ToString("N0", CultureInfo.InvariantCulture);
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

        private void RefreshFullUI()
        {
            if (_slotContainer == null) return;

            _slotContainer.Clear();
            _activeSlots.Clear();

            // Opening the bag re-reads the balance, so it is always truth even if an event was missed.
            SetBalanceText(_wallet?.Balance ?? 0);

            if (_inventory == null) return;

            foreach (var item in _inventory.CurrentItems)
            {
                UpdateOrSpawnSlot(item);
            }

            if (_emptyHint != null)
            {
                _emptyHint.style.display = _inventory.CurrentItems.Count == 0
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
        }

        private void UpdateOrSpawnSlot(InventoryItem item)
        {
            if (_slotContainer == null) return;

            if (_activeSlots.TryGetValue(item.ItemID, out VisualElement existingSlot))
            {
                UpdateSlotVisuals(existingSlot, item.Quantity);
            }
            else
            {
                ItemData data = _itemDatabase.GetItemByID(item.ItemID);
                if (data == null) return;

                VisualElement newSlot = _slotTemplate != null ? _slotTemplate.Instantiate() : CreateFallbackSlot();
                
                // Set icon
                var iconElement = newSlot.Q<VisualElement>("ItemIcon");
                if (iconElement != null && data.Icon != null)
                {
                    iconElement.style.backgroundImage = new StyleBackground(data.Icon);
                }

                // Set Name
                var nameLabel = newSlot.Q<Label>("ItemName");
                if (nameLabel != null)
                {
                    nameLabel.text = data.DisplayName;
                }

                // Set quantity
                UpdateSlotVisuals(newSlot, item.Quantity);

                // Setup Drop Button
                var dropButton = newSlot.Q<Button>("DropButton");
                if (dropButton != null)
                {
                    dropButton.clicked += () =>
                    {
                        _inventory?.DropItem(item.ItemID, 1);
                    };
                }

                _slotContainer.Add(newSlot);
                _activeSlots.Add(item.ItemID, newSlot);
            }
        }

        private void UpdateSlotVisuals(VisualElement slot, int quantity)
        {
            var qtyLabel = slot.Q<Label>("ItemQuantity");
            if (qtyLabel != null)
            {
                qtyLabel.text = $"×{quantity}";
            }
        }

        private void RemoveSlot(string itemID)
        {
            if (_activeSlots.TryGetValue(itemID, out VisualElement existingSlot))
            {
                if (_slotContainer != null && _slotContainer.Contains(existingSlot))
                {
                    _slotContainer.Remove(existingSlot);
                }
                _activeSlots.Remove(itemID);
            }
        }

        private VisualElement CreateFallbackSlot()
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.marginBottom = 10;
            container.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            container.style.paddingTop = 5;
            container.style.paddingBottom = 5;
            container.style.paddingLeft = 5;
            container.style.paddingRight = 5;
            container.style.borderTopLeftRadius = 5;
            container.style.borderTopRightRadius = 5;
            container.style.borderBottomLeftRadius = 5;
            container.style.borderBottomRightRadius = 5;

            var icon = new VisualElement();
            icon.name = "ItemIcon";
            icon.style.width = 50;
            icon.style.height = 50;
            icon.style.marginRight = 10;
            container.Add(icon);

            var name = new Label();
            name.name = "ItemName";
            name.style.flexGrow = 1;
            name.style.color = Color.white;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            container.Add(name);

            var qty = new Label();
            qty.name = "ItemQuantity";
            qty.style.marginRight = 15;
            qty.style.color = new Color(1f, 0.8f, 0.2f); // yellowish
            qty.style.fontSize = 16;
            container.Add(qty);

            var btn = new Button();
            btn.name = "DropButton";
            btn.text = "Drop";
            btn.style.backgroundColor = new Color(0.8f, 0.2f, 0.2f);
            btn.style.color = Color.white;
            container.Add(btn);

            return container;
        }
    }
}