using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Reflex-injected wrapper over the master save store, exposing <see cref="ISaveService"/>.
    ///
    /// Writes are serialised AND coalesced. Callers fire <see cref="SaveAsync"/> after every mutation
    /// (see <see cref="InventoryService.AddItemToInventory"/>), so several saves can be requested in a
    /// single frame; letting those overlap made two threads open the same file at once and throw
    /// <c>IOException: Sharing violation</c>. At most one write is now ever in flight, and a save
    /// requested while one is running replaces the pending snapshot rather than queueing behind it —
    /// only the newest state is worth writing, so N rapid saves cost one extra write, not N.
    /// </summary>
    public sealed class SaveService : ISaveService, IDisposable
    {
        private const string SAVE_FILE_NAME = "dumyat_master_save.json";
        private const string TEMP_SUFFIX = ".tmp";

        private GameSaveData _currentSaveData;

        // Cached on construction: Unity APIs like persistentDataPath must not be touched off the main
        // thread, and the writer runs on the thread pool.
        private readonly string _savePath;
        private readonly string _tempPath;

        private readonly object _gate = new object();

        // The newest serialised snapshot awaiting a write, or null when nothing is pending.
        private string _pendingJson;

        // True while the writer loop is draining _pendingJson. Guarded by _gate.
        private bool _writerRunning;

        // The in-flight writer, so FlushAsync can wait for it. Guarded by _gate.
        private Task _writerTask;

        // How long shutdown will block for a pending write before giving up and letting the app close.
        private const int QUIT_FLUSH_TIMEOUT_MS = 2000;

        public SaveService()
        {
            _savePath = Path.Combine(Application.persistentDataPath, SAVE_FILE_NAME);
            _tempPath = _savePath + TEMP_SUFFIX;

            // Self-wiring rather than relying on a MonoBehaviour being present in every scene: durability
            // on quit is this service's own responsibility, and a component a designer must remember to
            // add is exactly the kind of thing that silently goes missing in one scene.
            Application.quitting += FlushOnQuit;
        }

        public void Dispose() => Application.quitting -= FlushOnQuit;

        public GameSaveData CurrentSaveData
        {
            get
            {
                if (_currentSaveData == null)
                {
                    Load();
                }
                return _currentSaveData;
            }
        }

        public void Load()
        {
            if (File.Exists(_savePath))
            {
                try
                {
                    string json = File.ReadAllText(_savePath);
                    _currentSaveData = new GameSaveData();
                    JsonUtility.FromJsonOverwrite(json, _currentSaveData);
                    Debug.Log("SaveService: Master Save Loaded.");
                    return;
                }
                catch (Exception e)
                {
                    // A save that cannot be read must not take the game down — start fresh and say so
                    // loudly, rather than throwing out of a property getter deep in some consumer.
                    Debug.LogError($"SaveService: could not read '{_savePath}' — starting from a fresh save. {e}");
                }
            }
            else
            {
                Debug.Log("SaveService: No existing save file found. Initializing a fresh GameSaveData.");
            }

            _currentSaveData = new GameSaveData();
        }

        public void SaveAsync()
        {
            if (_currentSaveData == null) return;

            // Serialise on the calling (main) thread: JsonUtility is not safe to use off it, and this
            // also snapshots the data so later mutations cannot tear a write already in progress.
            string json = JsonUtility.ToJson(_currentSaveData, true);

            lock (_gate)
            {
                _pendingJson = json;
                if (_writerRunning) return;   // the running writer will pick this up when it loops
                _writerRunning = true;
                _writerTask = RunWriterAsync();
            }
        }

        /// <summary>
        /// Waits for the in-flight write, then writes anything still pending. Called automatically on
        /// <see cref="Application.quitting"/>; call it directly before any other point where losing the
        /// last save would matter.
        /// </summary>
        public async Task FlushAsync()
        {
            Task writer;
            lock (_gate) { writer = _writerTask; }

            // ConfigureAwait(false) throughout: FlushOnQuit blocks the main thread on this task, so it
            // must never need the main thread to resume — that would deadlock the shutdown.
            if (writer != null)
            {
                try { await writer.ConfigureAwait(false); }
                catch { /* RunWriterAsync already logged it */ }
            }

            // A save requested after the writer decided to stop has nobody to write it.
            string json;
            lock (_gate) { json = _pendingJson; _pendingJson = null; }
            if (json != null) WriteAtomic(json);
        }

        // Shutdown cannot await — Unity tears the app down as soon as the handlers return — so this
        // blocks briefly for the remaining write rather than losing it.
        private void FlushOnQuit()
        {
            try
            {
                if (!FlushAsync().Wait(QUIT_FLUSH_TIMEOUT_MS))
                    Debug.LogWarning($"SaveService: save still writing after {QUIT_FLUSH_TIMEOUT_MS}ms at quit — " +
                                     "the most recent changes may not have reached disk.");
            }
            catch (Exception e)
            {
                Debug.LogError($"SaveService: flush on quit failed — {e}");
            }
        }

        /// <summary>
        /// Drains <see cref="_pendingJson"/> until nothing is left. Exactly one of these runs at a time,
        /// which is what removes the file contention.
        /// </summary>
        private async Task RunWriterAsync()
        {
            try
            {
                while (true)
                {
                    string json;
                    lock (_gate)
                    {
                        if (_pendingJson == null)
                        {
                            _writerRunning = false;
                            return;
                        }
                        json = _pendingJson;
                        _pendingJson = null;
                    }

                    // See FlushAsync: this loop must be able to finish without the main thread.
                    await Task.Run(() => WriteAtomic(json)).ConfigureAwait(false);
                    Debug.Log($"SaveService: Master save written asynchronously to {_savePath}");
                }
            }
            catch (Exception e)
            {
                // Without this the exception would surface as an unhandled async fault with no context.
                lock (_gate) { _writerRunning = false; }
                Debug.LogError($"SaveService: failed to write master save to '{_savePath}' — {e}");
            }
        }

        /// <summary>
        /// Writes via a temp file and swaps it into place, so a crash or quit mid-write leaves the
        /// previous save intact instead of a half-written file that <see cref="Load"/> cannot parse.
        /// </summary>
        private void WriteAtomic(string json)
        {
            File.WriteAllText(_tempPath, json);

            if (File.Exists(_savePath)) File.Replace(_tempPath, _savePath, null);
            else File.Move(_tempPath, _savePath);
        }
    }
}
