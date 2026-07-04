namespace DumyatRuntime
{
    /// <summary>
    /// Reflex-injected wrapper over the master save store, exposing <see cref="ISaveService"/>.
    ///
    /// During the DI migration this delegates to the still-static <see cref="SaveManager"/> so that
    /// there is a single source of truth for <see cref="GameSaveData"/> while some managers have not
    /// yet been converted. Once every consumer resolves <see cref="ISaveService"/>, the file I/O in
    /// <see cref="SaveManager"/> can be folded into this class and the static removed.
    /// </summary>
    public sealed class SaveService : ISaveService
    {
        public GameSaveData CurrentSaveData
        {
            get
            {
                if (SaveManager.CurrentSaveData == null)
                {
                    SaveManager.LoadGame();
                }
                return SaveManager.CurrentSaveData;
            }
        }

        public void Load() => SaveManager.LoadGame();

        public void SaveAsync() => SaveManager.SaveGameAsync();
    }
}
