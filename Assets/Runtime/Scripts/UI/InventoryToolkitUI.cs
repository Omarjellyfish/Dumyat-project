using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

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

        private VisualElement _root;
        private VisualElement _slotContainer;
        private Dictionary<string, VisualElement> _activeSlots = new Dictionary<string, VisualElement>();

        private void OnEnable()
        {
            if (_uiDocument == null)
                _uiDocument = GetComponent<UIDocument>();

            if (_uiDocument == null) return;

            _root = _uiDocument.rootVisualElement;

            // The main container in your UI Builder document where slots will be added
            _slotContainer = _root.Q<VisualElement>("SlotContainer");

            InventoryManager.OnInventoryChanged += HandleInventoryChanged;

            _root.style.display = _startOpen ? DisplayStyle.Flex : DisplayStyle.None;
            
            if (_startOpen)
            {
                RefreshFullUI();
            }
        }

        private void OnDisable()
        {
            InventoryManager.OnInventoryChanged -= HandleInventoryChanged;
        }

        public void ToggleUI()
        {
            if (_root == null) return;
            
            bool isCurrentlyOpen = _root.style.display == DisplayStyle.Flex;
            if (isCurrentlyOpen)
            {
                CloseUI();
            }
            else
            {
                OpenUI();
            }
        }

        public void OpenUI()
        {
            if (_root == null) return;
            _root.style.display = DisplayStyle.Flex;
            RefreshFullUI();
        }

        public void CloseUI()
        {
            if (_root == null) return;
            _root.style.display = DisplayStyle.None;
        }

        private void HandleInventoryChanged(InventoryItem updatedItem)
        {
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

        private void RefreshFullUI()
        {
            if (_slotContainer == null) return;

            _slotContainer.Clear();
            _activeSlots.Clear();

            if (InventoryManager.Singleton == null) return;
            
            foreach (var item in InventoryManager.Singleton.CurrentItems)
            {
                UpdateOrSpawnSlot(item);
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
                        if (InventoryManager.Singleton != null)
                            InventoryManager.Singleton.DropItem(item.ItemID, 1);
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
                qtyLabel.text = quantity.ToString();
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
