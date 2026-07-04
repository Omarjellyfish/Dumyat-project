using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public class InventorySlotUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image _itemIcon;
        [SerializeField] private TextMeshProUGUI _quantityText;
        [SerializeField] private TextMeshProUGUI _nameText;

        private string _boundItemID;
        private IInventoryService _inventory;

        [Inject]
        private void Inject(IInventoryService inventory) => _inventory = inventory;
        // the UI slot with the item data and quantity
        public void Setup(ItemData itemData, int quantity)
        {
            _boundItemID = itemData.ItemID;
            
            if (_itemIcon != null)
                _itemIcon.sprite = itemData.Icon;
                
            if (_nameText != null)
                _nameText.text = itemData.DisplayName;

            UpdateQuantity(quantity);
        }

        public void UpdateQuantity(int quantity)
        {
            if (_quantityText != null)
            {
                _quantityText.text = quantity.ToString();
            }
        }

        // Hook this up to a UI Button component in the Inspector
        public void OnDropButtonClicked()
        {
            if (string.IsNullOrEmpty(_boundItemID)) return;

            // Drop 1 item when the button is clicked
            _inventory?.DropItem(_boundItemID, 1);
        }
    }
}
