using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The travel/door transition: carries the player across a scene load and drops them at the new
    /// scene's ArrivalPoint.
    ///
    /// It no longer owns any fading. <see cref="ScreenFadeService"/> blacks the view out from
    /// <see cref="SceneLoadBeginEvent"/>, which covers this path and the menu paths alike — this
    /// class only has to do the part that is specific to travelling: keeping the player alive across
    /// the load and placing them on arrival.
    /// </summary>
    public class FadeTransitionHandler : IFadeTransitionService
    {
        private readonly IEventService _eventService;
        private readonly ISceneLoadService _sceneLoadService;

        private bool _isTransitioning;
        private GameObject _playerObj;

        public bool IsTransitioning => _isTransitioning;

        public FadeTransitionHandler(IEventService eventService, ISceneLoadService sceneLoadService)
        {
            _eventService = eventService;
            _sceneLoadService = sceneLoadService;
            _eventService.Subscribe<SceneLoadCompleteEvent>(OnSceneLoadComplete);
        }

        public void StartTransition(string destinationScene)
        {
            if (_isTransitioning) return;

            _isTransitioning = true;

            // Find and persist the player
            _playerObj = GameObject.FindWithTag("Player");
            if (_playerObj != null)
            {
                if (_playerObj.transform.parent != null)
                {
                    _playerObj.transform.SetParent(null);
                }
                Object.DontDestroyOnLoad(_playerObj);
            }
            else
            {
                Debug.LogWarning("FadeTransitionHandler: Could not find GameObject with tag 'Player'.");
            }

            // SceneLoadService publishes SceneLoadBeginEvent immediately and then holds for
            // PreLoadHoldSeconds, which is what gives the blinder time to reach full black before
            // anything is destroyed.
            _sceneLoadService.LoadScene(destinationScene);
        }

        /// <summary>
        /// Places the player in the new scene. Runs synchronously inside the event dispatch, so it is
        /// guaranteed to finish before <see cref="ScreenFadeService"/>'s fade-in shows anything —
        /// that ease only advances on the following frames.
        /// </summary>
        private void OnSceneLoadComplete(SceneLoadCompleteEvent evt)
        {
            if (!_isTransitioning) return;

            if (_playerObj != null)
            {
                GameObject arrivalPoint = GameObject.FindWithTag("ArrivalPoint");
                if (arrivalPoint != null)
                {
                    _playerObj.transform.position = arrivalPoint.transform.position;
                    _playerObj.transform.rotation = arrivalPoint.transform.rotation;
                }
                else
                {
                    Debug.LogWarning("FadeTransitionHandler: Could not find GameObject with tag 'ArrivalPoint' in the new scene.");
                }

                // Move player into the newly loaded active scene so they don't stay in DontDestroyOnLoad
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_playerObj, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                _playerObj = null;
            }

            _isTransitioning = false;
        }
    }
}
