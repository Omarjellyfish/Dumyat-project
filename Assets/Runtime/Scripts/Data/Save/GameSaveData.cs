using System;

namespace DumyatRuntime
{
    [Serializable]
    public class GameSaveData
    {
        public InventorySaveData inventoryData = new InventorySaveData();

        public QuestSaveData questData = new QuestSaveData();
        public PersistenceSaveData persistenceData = new PersistenceSaveData();
        public SalesSaveData salesData = new SalesSaveData();
    }
}
