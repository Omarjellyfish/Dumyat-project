namespace DumyatRuntime
{
    /// <summary>
    /// Starting, resuming and leaving a play session — what the main menu and pause menu buttons
    /// actually do. Backed by <see cref="GameSessionService"/>.
    ///
    /// The menus depend on this rather than on <see cref="ISceneLoadService"/> and
    /// <see cref="ISaveService"/> directly, so "what New Game means" is defined in one place
    /// instead of once per button.
    /// </summary>
    public interface IGameSessionService
    {
        /// <summary>True when there is a session worth resuming — drives the Continue button.</summary>
        bool HasSave { get; }

        /// <summary>Scene <see cref="Continue"/> will load. Empty when <see cref="HasSave"/> is false.</summary>
        string ResumeSceneName { get; }

        /// <summary>UTC stamp of the resume point, for display. Empty when there is no save.</summary>
        string ResumeSavedAtUtc { get; }

        /// <summary>Wipes the save and loads the configured starting scene. Destructive.</summary>
        void NewGame();

        /// <summary>Loads <see cref="ResumeSceneName"/>. Does nothing (and warns) if there is no save.</summary>
        void Continue();

        /// <summary>Unpauses, flushes the save and returns to the main menu scene.</summary>
        void ExitToMainMenu();

        /// <summary>Flushes the save, then quits the player (and stops play mode in the Editor).</summary>
        void QuitApplication();
    }
}
