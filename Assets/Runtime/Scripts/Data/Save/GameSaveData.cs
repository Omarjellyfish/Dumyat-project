using System;

namespace DumyatRuntime
{
    [Serializable]
    public class GameSaveData
    {
        public InventorySaveData inventoryData = new InventorySaveData();

        public QuestSaveData questData = new QuestSaveData();
        public PersistenceSaveData persistenceData = new PersistenceSaveData();
        public CraftingSaveData craftingData = new CraftingSaveData();
        public SalesSaveData salesData = new SalesSaveData();
        public WalletSaveData walletData = new WalletSaveData();
        public AchievementSaveData achievementData = new AchievementSaveData();
        public TutorialSaveData tutorialData = new TutorialSaveData();
        public SessionSaveData sessionData = new SessionSaveData();
    }
}
