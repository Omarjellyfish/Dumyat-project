using System;
using System.Collections.Generic;

namespace DumyatRuntime
{
    [Serializable]
    public class InventorySaveData
    {
        public List<InventoryItem> items = new List<InventoryItem>();
    }
}
