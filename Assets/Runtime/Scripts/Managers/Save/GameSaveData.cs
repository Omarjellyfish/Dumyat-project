using System;

namespace DumyatRuntime
{
    [Serializable]
    public class GameSaveData
    {
        public InventorySaveData inventoryData = new InventorySaveData();

        // Future expansions will go here!
        // public PlayerSaveData playerData = new PlayerSaveData();
        // public QuestSaveData questData = new QuestSaveData();
    }
}
