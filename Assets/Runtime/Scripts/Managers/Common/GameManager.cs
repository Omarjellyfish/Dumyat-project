using UnityEngine;

namespace DumyatRuntime
{
    public enum GameState
    {
        MainMenu=0,
        FreeRoaming=1,
        Paused=2,
        InQuest=3,

    }
    public class GameManager : MonoBehaviour, IManager
    {
        public static GameManager Singleton { get; private set; }

        [SerializeField] private GameState _currentState = GameState.MainMenu;

        private void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }
            Singleton = this;
            DontDestroyOnLoad(gameObject);
        }

        public GameState GetCurrentState()
        {
            return _currentState;
        }

        public void SetCurrentState(GameState newState)
        {
            if (_currentState == newState)
                return;
            _currentState = newState;
            HandleGlobalSystems(_currentState);
            EventBus.Publish(new GameStateChangedEvent(_currentState));
        }

        private void HandleGlobalSystems(GameState state)
        {
            switch (state)
            {
                case GameState.Paused:
                    Time.timeScale = 0f;
                    break;

                default:
                    Time.timeScale = 1f;
                    break;
            }
        }
    }
}
