using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    /// <summary>
    /// Returns the fishing rod to its stand when the player loses it.
    ///
    /// It used to be a trigger and nothing else, and that trigger was the fishing area volume this
    /// component shares a GameObject with — whose top in Ras El Bar sits at y = -8.75, while the water
    /// has a solid mesh collider at y = -1.22. A dropped rod therefore came to rest ON the water, seven
    /// and a half metres ABOVE the only thing that could recover it, and simply stayed there. The rod
    /// was unreachable and there was no way to get it back short of reloading the scene.
    ///
    /// So recovery no longer depends on the rod falling into a particular box. A watchdog returns it
    /// whenever it has been left out of reach — too far from its stand, or too far below it — for long
    /// enough that the player has clearly lost it rather than just put it down. The trigger still works
    /// and still catches a rod that does fall in; it is now the fast path, not the only path.
    /// </summary>
    public class FishingRodSpawner : MonoBehaviour
    {
        [Tooltip("The rod in the scene. Despite the name this is a scene instance, not a prefab — the " +
                 "field keeps its old name so existing scene wiring still resolves. Found by the 'Rod' " +
                 "tag at runtime if left empty.")]
        [SerializeField] private GameObject _fishingRodPrefab;

        [Tooltip("Where the rod is returned to.")]
        [SerializeField] private Transform _spawnPoint;

        [Header("Recovery")]
        [Tooltip("Optional. Leave empty to use the project's FishingSettings asset from Resources.")]
        [SerializeField] private FishingSettingsSO _settings;

        [Tooltip("Fallback: how long the rod is left where it fell before being returned.")]
        [SerializeField] private float _recoverySeconds = 6f;

        [Tooltip("Fallback: a rod further than this from its stand is out of play.")]
        [SerializeField] private float _maxDistance = 25f;

        [Tooltip("A rod this far below the stand is in the water or through the floor, however close " +
                 "it is horizontally.")]
        [SerializeField] private float _recoverBelow = 3f;

        [Tooltip("Rod tag used to find it, and to recognise it in the trigger.")]
        [SerializeField] private string _rodTag = "Rod";

        private Transform _rod;
        private XRGrabInteractable _rodInteractable;
        private float _outOfPlaceSince;

        private float RecoverySeconds => _settings != null ? _settings.RodRecoverySeconds : _recoverySeconds;
        private float MaxDistance => _settings != null ? _settings.RodMaxDistance : _maxDistance;

        private void Awake()
        {
            if (_settings == null) _settings = FishingSettingsSO.Load();

            if (_spawnPoint == null)
            {
                Debug.LogWarning($"'{name}' has no spawn point assigned — the rod cannot be recovered.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (_spawnPoint == null) return;
            if (!ResolveRod()) return;

            // Held, or being held by a socket: not lost, whatever its position. Snatching a rod out of
            // the player's hand because they leaned over the gunwale would be worse than the bug.
            if (_rodInteractable != null && _rodInteractable.isSelected)
            {
                _outOfPlaceSince = 0f;
                return;
            }

            bool tooFar = Vector3.Distance(_rod.position, _spawnPoint.position) > MaxDistance;
            bool tooLow = _rod.position.y < _spawnPoint.position.y - _recoverBelow;

            if (!tooFar && !tooLow)
            {
                _outOfPlaceSince = 0f;
                return;
            }

            if (_outOfPlaceSince == 0f)
            {
                _outOfPlaceSince = Time.time;
                return;
            }

            if (Time.time - _outOfPlaceSince < RecoverySeconds) return;

            ReturnRod(tooLow ? "below its stand" : "out of reach");
        }

        private bool ResolveRod()
        {
            if (_rod != null) return true;

            GameObject rodObject = _fishingRodPrefab;

            if (rodObject == null && !string.IsNullOrEmpty(_rodTag))
            {
                rodObject = GameObject.FindGameObjectWithTag(_rodTag);
            }

            if (rodObject == null) return false;

            _rod = rodObject.transform;
            _rodInteractable = rodObject.GetComponentInParent<XRGrabInteractable>()
                               ?? rodObject.GetComponentInChildren<XRGrabInteractable>();

            return true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other == null || !other.CompareTag(_rodTag)) return;
            if (_spawnPoint == null) return;

            // Resolve to the body that owns the collider. Writing to other.transform moved whichever
            // child collider happened to enter and left the rest of the rod behind — the hook has its
            // own Rigidbody on this prefab, so that was a real possibility rather than a theoretical one.
            Rigidbody body = other.attachedRigidbody;
            Transform target = body != null ? body.transform : other.transform;

            if (_rodInteractable != null && _rodInteractable.isSelected) return;

            Place(target);
            Debug.Log($"Rod fell in and was returned to its stand.", this);
        }

        private void ReturnRod(string reason)
        {
            Place(_rod);
            _outOfPlaceSince = 0f;

            Debug.Log($"Rod was {reason} for {RecoverySeconds:0.#}s and has been returned to its stand.", this);
        }

        private void Place(Transform target)
        {
            if (target == null) return;

            target.SetPositionAndRotation(_spawnPoint.position, _spawnPoint.rotation);

            // Land it still. Carrying the throw velocity across the teleport just launches it straight
            // back off the stand.
            if (target.TryGetComponent(out Rigidbody body) && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
    }
}
