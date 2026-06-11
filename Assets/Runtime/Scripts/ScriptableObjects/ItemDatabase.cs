using NUnit.Framework.Interfaces;
using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "Dumyat/Inventory/Database")]
    public class ItemDatabase : ScriptableObject
    {
        public List<ItemData> allItems = new List<ItemData>();

        public ItemData GetItemByID(string id)
        {
            foreach (var item in allItems)
            {
                if (item.itemID == id) return item;
            }

            Debug.LogWarning($"Item ID {id} not found in the database!");
            return null;
        }
    }
}