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
        private const string PlayerTag = "Player";

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
            _playerObj = FindPlayerRig();
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

                // The rig is carried across, never destroyed and recreated, so nothing under it gets a
                // fresh OnEnable and every XRI registration it holds still points at the old scene's
                // manager — which has just been destroyed. See CarriedRigInteraction; the tok-tok
                // carries the rig the same way and calls the same thing.
                CarriedRigInteraction.Rebind(_playerObj);

                _playerObj = null;
            }

            _isTransitioning = false;
        }

        /// <summary>
        /// The rig that travels. Tag alone is not enough to identify it.
        ///
        /// <c>FindWithTag</c> returns an ARBITRARY one of the active matches, and a scene only has to
        /// gain one stray object tagged Player for travel out of it to become a coin toss — Ezbet Al
        /// Bourg carried a non-VR "Player (Test Stand-in)" capsule for exactly this long. Losing that
        /// toss carries the stand-in and destroys the real rig with the old scene: the player arrives
        /// with no interactors, <see cref="PlayerRigSpawner"/> sees no camera and spawns a second rig
        /// on top of the stray, and every ray then hits the stray's collider instead of the world.
        ///
        /// So the camera decides. <c>Camera.main</c> is the one thing on the rig that cannot be
        /// duplicated by a leftover marker without also breaking rendering, and its root is the rig —
        /// the same identification <see cref="PlayerRigSpawner.SceneHasRig"/> already trusts. The tag
        /// stays as the fallback for a scene whose rig has no enabled main camera yet.
        /// </summary>
        private static GameObject FindPlayerRig()
        {
            GameObject[] tagged = GameObject.FindGameObjectsWithTag(PlayerTag);

            if (tagged.Length <= 1)
                return tagged.Length == 1 ? tagged[0] : null;

            GameObject cameraRoot = Camera.main != null ? Camera.main.transform.root.gameObject : null;
            bool cameraRootIsTagged = cameraRoot != null && cameraRoot.CompareTag(PlayerTag);

            Debug.LogWarning($"FadeTransitionHandler: {tagged.Length} active objects are tagged " +
                             $"'{PlayerTag}' ({string.Join(", ", System.Array.ConvertAll(tagged, o => o.name))}). " +
                             "Only the rig should be. Travelling with " +
                             $"'{(cameraRootIsTagged ? cameraRoot.name : tagged[0].name)}' — untag or remove the rest, " +
                             "or the wrong one will eventually be carried across a load.");

            return cameraRootIsTagged ? cameraRoot : tagged[0];
        }

    }
}