using System.Threading.Tasks;

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

        /// <summary>
        /// Wipes every slice back to its defaults for a New Game, and writes the empty save.
        /// <see cref="CurrentSaveData"/> and each of its slice objects keep their identity, so
        /// services that cached a slice reference stay valid.
        /// </summary>
        void ResetSave();

        /// <summary>
        /// Completes once every requested save has reached disk. <see cref="SaveAsync"/> coalesces
        /// writes, so the newest state can still be pending when the game shuts down; await this to
        /// guarantee it is durable. Safe to call when nothing is pending (completes immediately).
        /// </summary>
        Task FlushAsync();
    }
}
