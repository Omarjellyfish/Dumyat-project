namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for the master save store. Replaces direct static access to
    /// <see cref="SaveManager"/> so consumers depend on this interface (resolved by Reflex)
    /// rather than a global. See CLAUDE.md ▸ "Managers &amp; services = Reflex DI".
    /// </summary>
    public interface ISaveService
    {
        /// <summary>The shared, in-memory aggregate save blob (loaded on first access).</summary>
        GameSaveData CurrentSaveData { get; }

        /// <summary>Loads the master save from disk (or initialises a fresh one).</summary>
        void Load();

        /// <summary>Serialises <see cref="CurrentSaveData"/> to disk asynchronously.</summary>
        void SaveAsync();
    }
}
