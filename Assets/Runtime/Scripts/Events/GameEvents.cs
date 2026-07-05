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
}
