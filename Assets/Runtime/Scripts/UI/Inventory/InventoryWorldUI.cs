using System.Collections.Generic;
using UnityEngine;
using Reflex.Attributes;
using Reflex.Extensions;
using Reflex.Injectors;

namespace DumyatRuntime
{
    public class InventoryWorldUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ItemDatabase _itemDatabase;
        [SerializeField] private Transform _slotContainer;
        [SerializeField] private InventorySlotUI _slotPrefab;
        [SerializeField] private GameObject _uiPanel;

        [Header("Settings")]
        [SerializeField] private bool _startOpen = false;

        private IInventoryService _inventory;
        private IEventService _eventService;

        [Inject]
        private void Inject(IInventoryService inventory, IEventService eventService)
        {
            _inventory = inventory;
            _eventService = eventService;
        }

        // Keep track of instantiated slots by item ID to easily update them
        private Dictionary<string, InventorySlotUI> _activeSlots = new Dictionary<string, InventorySlotUI>();

        private void Start()
        {
            if (_uiPanel != null)
            {
                _uiPanel.SetActive(_startOpen);
            }
            RefreshFullUI();
        }

        private void OnEnable()
        {
            _eventService.Subscribe<InventoryChangedEvent>(HandleInventoryChanged);
        }

        private void OnDisable()
        {
            _eventService.Unsubscribe<InventoryChangedEvent>(HandleInventoryChanged);
        }

        public void ToggleUI()
        {
            if (_uiPanel != null)
            {
                bool isActive = !_uiPanel.activeSelf;
                _uiPanel.SetActive(isActive);
                
                if (isActive)
                {
                    RefreshFullUI();
                }
            }
        }

        public void OpenUI()
        {
            if (_uiPanel != null && !_uiPanel.activeSelf)
            {
                _uiPanel.SetActive(true);
                RefreshFullUI();
            }
        }

        public void CloseUI()
        {
            if (_uiPanel != null && _uiPanel.activeSelf)
            {
                _uiPanel.SetActive(false);
            }
        }

        private void HandleInventoryChanged(InventoryChangedEvent evt)
        {
            InventoryItem updatedItem = evt.Item;

            // If the UI is closed, don't bother updating individual slots right now,
            // we will rebuild it when opened.
            if (_uiPanel != null && !_uiPanel.activeSelf) return;

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
            // Clear existing
            foreach (var slot in _activeSlots.Values)
            {
                if (slot != null) Destroy(slot.gameObject);
            }
            _activeSlots.Clear();

            // Rebuild
            if (_inventory == null) return;

            foreach (var item in _inventory.CurrentItems)
            {
                UpdateOrSpawnSlot(item);
            }
        }

        private void UpdateOrSpawnSlot(InventoryItem item)
        {
            if (_activeSlots.TryGetValue(item.ItemID, out InventorySlotUI existingSlot))
            {
                existingSlot.UpdateQuantity(item.Quantity);
            }
            else
            {
                ItemData data = _itemDatabase.GetItemByID(item.ItemID);
                if (data == null) return;

                InventorySlotUI newSlot = Instantiate(_slotPrefab, _slotContainer);
                // Runtime-instantiated slot: inject so its drop button can reach IInventoryService.
                GameObjectInjector.InjectRecursive(newSlot.gameObject, gameObject.scene.GetSceneContainer());
                newSlot.Setup(data, item.Quantity);
                _activeSlots.Add(item.ItemID, newSlot);
            }
        }

        private void RemoveSlot(string itemID)
        {
            if (_activeSlots.TryGetValue(itemID, out InventorySlotUI existingSlot))
            {
                if (existingSlot != null)
                {
                    Destroy(existingSlot.gameObject);
                }
                _activeSlots.Remove(itemID);
            }
        }
    }
}
