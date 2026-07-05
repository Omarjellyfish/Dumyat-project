using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace DumyatRuntime
{
    [Serializable]
    public class InventoryItem
    {
        [FormerlySerializedAs("itemID")]
        [SerializeField] private string _itemID;

        [FormerlySerializedAs("quantity")]
        [SerializeField] private int _quantity;

        public string ItemID => _itemID;
        public int Quantity 
        { 
            get => _quantity; 
            set => _quantity = value; 
        }

        public InventoryItem(string id, int amount)
        {
            _itemID = id;
            _quantity = amount;
        }
    }
}