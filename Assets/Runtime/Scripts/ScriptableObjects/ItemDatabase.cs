using NUnit.Framework.Interfaces;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "Dumyat/Inventory/Database")]
    public class ItemDatabase : ScriptableObject
    {
        [FormerlySerializedAs("allItems")]
        [SerializeField] private List<ItemData> _allItems = new List<ItemData>();

        public IReadOnlyList<ItemData> AllItems => _allItems;

        public ItemData GetItemByID(string id)
        {
            foreach (var item in _allItems)
            {
                if (item.ItemID == id) return item;
            }

            Debug.LogWarning($"Item ID {id} not found in the database!");
            return null;
        }
    }
}