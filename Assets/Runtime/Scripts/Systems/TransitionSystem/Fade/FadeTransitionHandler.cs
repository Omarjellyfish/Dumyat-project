using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

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

                    // Heading only — see PlayerSpawnPose. Copying the marker's full rotation used to
                    // land the player leaning, because several ArrivalPoints carry a few degrees of
                    // pitch that their Euler hints do not show.
                    _playerObj.transform.rotation = PlayerSpawnPose.UprightFacing(arrivalPoint.transform);
                }
                else
                {
                    Debug.LogWarning("FadeTransitionHandler: Could not find GameObject with tag 'ArrivalPoint' in the new scene.");
                }

                // Move player into the newly loaded active scene so they don't stay in DontDestroyOnLoad
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_playerObj, UnityEngine.SceneManagement.SceneManager.GetActiveScene());

                // The rig is carried across, never destroyed/recreated, so its Near-Far Interactors
                // never get a fresh OnEnable during this transition. They stay registered with the
                // OLD scene's XRInteractionManager, which is now gone — leaving the ray stuck as if
                // permanently grabbing, with a "Missing" Interaction Manager reference in the
                // Inspector. Toggling the component forces it to re-register against whatever
                // XRInteractionManager is in the new scene.
                ReenableNearFarInteractors(_playerObj);

                _playerObj = null;
            }

            _isTransitioning = false;
        }

        private static void ReenableNearFarInteractors(GameObject playerObj)
        {
            // The Near-Far Interactor's parent controller carries an XRInteractionGroup, and the
            // interactor is only allowed to register with the XRInteractionManager AFTER its
            // containing group has registered itself. Toggling only the interactor was not enough
            // — the group's own OnEnable never got a fresh trigger, so it still wasn't registered
            // when the interactor tried, producing the same "before its containing Interaction
            // Group is registered" warning as the original bug. Toggle the group first, then the
            // interactor, so each one's OnEnable runs in the order XRI actually requires.
            foreach (var group in playerObj.GetComponentsInChildren<XRInteractionGroup>(true))
            {
                group.enabled = false;
                group.enabled = true;
            }

            foreach (var nearFar in playerObj.GetComponentsInChildren<NearFarInteractor>(true))
            {
                nearFar.enabled = false;
                nearFar.enabled = true;
            }
        }
    }
}