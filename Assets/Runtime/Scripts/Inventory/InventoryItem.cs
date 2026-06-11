using System;

namespace DumyatRuntime
{
    [Serializable]
    public class InventoryItem
    {
        public string itemID;

        public int quantity;



        public InventoryItem(string id, int amount)
        {
            itemID = id;
            quantity = amount;
            
        }
    }
}