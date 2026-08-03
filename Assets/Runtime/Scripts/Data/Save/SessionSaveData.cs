using System;

namespace DumyatRuntime
{
    /// <summary>
    /// Where the player was, so the main menu's Continue button knows what to load.
    /// Written by <see cref="GameSessionService"/> on every resumable scene load.
    /// </summary>
    [Serializable]
    public class SessionSaveData
    {
        /// <summary>
        /// False until the player has actually entered a gameplay scene once. Continue is disabled
        /// while this is false, which is also what an existing pre-session save looks like after
        /// JsonUtility.FromJsonOverwrite leaves this field at its default — it self-heals on the
        /// next scene load.
        /// </summary>
        public bool hasSave;

        /// <summary>Scene name to return to. Empty when <see cref="hasSave"/> is false.</summary>
        public string lastSceneName = "";

        /// <summary>Round-trip ("o") UTC stamp of the last resumable scene load, for the menu's sub-label.</summary>
        public string savedAtUtc = "";
    }
}
