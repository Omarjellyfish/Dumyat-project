namespace DumyatRuntime
{
    /// <summary>Published by <see cref="GameStateService"/> whenever the active <see cref="GameState"/> changes.</summary>
    public readonly struct GameStateChangedEvent : IGameEvent
    {
        public readonly GameState NewState;

        public GameStateChangedEvent(GameState newState)
        {
            NewState = newState;
        }
    }

    /// <summary>Published when the game is paused or resumed (e.g. by the pause menu).</summary>
    public readonly struct GamePauseToggledEvent : IGameEvent
    {
        public readonly bool IsPaused;

        public GamePauseToggledEvent(bool isPaused)
        {
            IsPaused = isPaused;
        }
    }

    /// <summary>
    /// The player asked to open or close the pause menu. Published by input (see
    /// <see cref="PauseInputRelay"/>), never by the menu itself — that split is what lets a wrist
    /// button, a debug key or the XR simulator open pause without the menu knowing about any of them.
    /// </summary>
    public readonly struct PauseToggleRequestedEvent : IGameEvent { }

    /// <summary>
    /// The save was wiped for a New Game. Anything holding derived state built from save data
    /// should rebuild it rather than trusting what it cached.
    /// </summary>
    public readonly struct SaveResetEvent : IGameEvent { }
}
