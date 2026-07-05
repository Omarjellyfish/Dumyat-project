namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for the global game-state machine. Injected in place of
    /// <c>GameManager.Singleton</c>. Backed by <see cref="GameStateService"/>. Owns the
    /// <see cref="GameState"/> value and publishes <see cref="GameStateChangedEvent"/>.
    /// </summary>
    public interface IGameStateService
    {
        GameState CurrentState { get; }
        void SetState(GameState newState);
    }
}
