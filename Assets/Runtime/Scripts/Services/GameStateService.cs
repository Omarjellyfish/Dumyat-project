using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The app-wide game states. (Relocated here from the former <c>GameManager</c>.)
    /// </summary>
    public enum GameState
    {
        MainMenu = 0,
        FreeRoaming = 1,
        Paused = 2,
        InQuest = 3,
    }

    /// <summary>
    /// Plain C# implementation of <see cref="IGameStateService"/>, constructed by Reflex.
    /// Replaces the former <c>GameManager</c> singleton: owns the current <see cref="GameState"/>,
    /// pauses time on <see cref="GameState.Paused"/>, and publishes <see cref="GameStateChangedEvent"/>.
    /// Registered eagerly so it exists for the whole app lifetime.
    /// </summary>
    public sealed class GameStateService : IGameStateService
    {
        private GameState _currentState = GameState.MainMenu;

        public GameState CurrentState => _currentState;

        public void SetState(GameState newState)
        {
            if (_currentState == newState) return;
            _currentState = newState;
            HandleGlobalSystems(_currentState);
            EventBus.Publish(new GameStateChangedEvent(_currentState));
        }

        private void HandleGlobalSystems(GameState state)
        {
            Time.timeScale = state == GameState.Paused ? 0f : 1f;
        }
    }
}
