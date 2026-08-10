using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// Lives on the Car GameObject. The car persists across all scene loads
    /// via DontDestroyOnLoad. It parks at a waiting point in every scene
    /// and is always available for the player to use again.
    /// 
    /// Flow: BusStation calls <see cref="RequestPickup"/> → car tweens to pickup point →
    /// player is seated → scene loads → car parks at arrival point → player exits → car waits.
    /// </summary>
    public class CarTransitionHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform _playerSeatPoint;
        [SerializeField] private Transform _exitPoint;

        [Header("Tween Config")]
        [SerializeField] private float _driveSpeed = 8f;
        [SerializeField] private float _arrivalThreshold = 0.1f;

        [Header("Scene Config")]
        [SerializeField] private string _arrivalPointTag = "ArrivalPoint";

        private IEventService _eventService;
        private ISceneLoadService _sceneLoadService;
        private Transform _playerRoot;
        private bool _isTransitioning;
        private bool _isPersistent;

        [Inject]
        private void Inject(IEventService eventService, ISceneLoadService sceneLoadService)
        {
            _eventService = eventService;
            _sceneLoadService = sceneLoadService;
        }

        private void OnEnable()
        {
            if (_eventService != null)
                _eventService.Subscribe<SceneLoadCompleteEvent>(OnSceneLoadComplete);
        }

        private void OnDisable()
        {
            if (_eventService != null)
                _eventService.Unsubscribe<SceneLoadCompleteEvent>(OnSceneLoadComplete);
        }

        /// <summary>
        /// Called by <see cref="BusStation"/> when the player requests a ride.
        /// The car drives to the pickup point, seats the player, then starts loading.
        /// </summary>
        public void RequestPickup(Transform pickupPoint, Transform playerRoot, string destinationScene)
        {
            if (_isTransitioning) return;

            _isTransitioning = true;
            _playerRoot = playerRoot;

            StartCoroutine(DriveToPickupAndTravel(pickupPoint, destinationScene));
        }

        private IEnumerator DriveToPickupAndTravel(Transform pickupPoint, string destinationScene)
        {
            Vector3 targetPosition = pickupPoint.position;
            Quaternion targetRotation = pickupPoint.rotation;

            // Smoothly drive toward the pickup point
            while (Vector3.Distance(transform.position, targetPosition) > _arrivalThreshold)
            {
                float step = _driveSpeed * Time.deltaTime;
                transform.position = Vector3.MoveTowards(transform.position, targetPosition, step);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, step * 30f);
                yield return null;
            }

            // Snap to exact position
            transform.position = targetPosition;
            transform.rotation = targetRotation;

            // Seat the player inside the car
            _playerRoot.SetParent(transform);
            _playerRoot.position = _playerSeatPoint.localPosition;
            _playerRoot.rotation = _playerSeatPoint.localRotation;

            // Mark as persistent the first time
            if (!_isPersistent)
            {
                if (transform.parent != null) transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
                _isPersistent = true;
            }

            // Start loading the destination scene
            if (_sceneLoadService != null)
            {
                _sceneLoadService.LoadScene(destinationScene);
            }
            else
            {
                Debug.LogError("CarTransitionHandler: ISceneLoadService is null! Reflex failed to inject it. Is there a SceneScope in this scene?");
            }
        }

        private void OnSceneLoadComplete(SceneLoadCompleteEvent evt)
        {
            if (!_isTransitioning) return;

            // Park the car at this scene's waiting point
            GameObject arrivalPoint = GameObject.FindWithTag(_arrivalPointTag);

            if (arrivalPoint != null)
            {
                transform.position = arrivalPoint.transform.position;
                transform.rotation = arrivalPoint.transform.rotation;
            }

            // Eject the player
            if (_playerRoot != null)
            {
                _playerRoot.SetParent(null);

                // Place the player at the exit point (next to the car door)
                if (_exitPoint != null)
                {
                    _playerRoot.position = _exitPoint.position;
                    _playerRoot.rotation = _exitPoint.rotation;
                }

                // Move player into the active scene so they behave normally
                SceneManager.MoveGameObjectToScene(_playerRoot.gameObject, SceneManager.GetActiveScene());

                // Carried, not respawned — so nothing under the rig gets a fresh OnEnable and its XRI
                // registrations still point at the manager that just died with the old scene. Without
                // this the player arrives able to walk but unable to grab anything, the bag included.
                // The bus handler carries the rig the same way and calls the same thing.
                CarriedRigInteraction.Rebind(_playerRoot.gameObject);

                _playerRoot = null;
            }

            // Car stays alive and parked — ready for next trip
            _isTransitioning = false;
        }
    }
}
