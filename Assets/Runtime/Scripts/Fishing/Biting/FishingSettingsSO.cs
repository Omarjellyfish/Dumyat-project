using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// One asset holding every number that decides how fishing feels. Drop it in
    /// <c>Assets/Runtime/Resources/Fishing/</c> and the fishing components find it on their own — no
    /// prefab wiring, no per-scene setup, and changing the bite window is a one-field edit rather than
    /// hunting the same value across a prefab and four scenes.
    ///
    /// It exists because the tuning was scattered and contradictory: the swim speed was authored in
    /// three places (code default 3, prefab 6, scene override 25) and the arrival distance in two, and
    /// the values that actually shipped were the ones nobody had looked at. Everything here is read at
    /// runtime, so a component that predates this asset keeps working on its own serialized defaults.
    /// </summary>
    [CreateAssetMenu(menuName = "Dumyat/Fishing/Fishing Settings", fileName = "FishingSettings")]
    public class FishingSettingsSO : ScriptableObject
    {
        /// <summary>Where the components look when nothing is assigned in the Inspector.</summary>
        public const string ResourcePath = "Fishing/FishingSettings";

        [Header("Bite Window")]
        [Tooltip("Earliest a fish may reach the hook after the line settles, in seconds.")]
        [Min(0f)] [SerializeField] private float _minBiteSeconds = 15f;

        [Tooltip("Latest a fish may reach the hook, in seconds. This is a guarantee, not a target — " +
                 "if nothing has arrived by now a fish is placed on the hook.")]
        [Min(0f)] [SerializeField] private float _maxBiteSeconds = 45f;

        [Header("Approach")]
        [Tooltip("The pace a fish sets off at. It speeds up automatically if it is running late.")]
        [Min(0.1f)] [SerializeField] private float _cruiseSpeed = 1.6f;

        [Tooltip("Hard ceiling on approach speed, however late the fish is running.")]
        [Min(0.1f)] [SerializeField] private float _maxApproachSpeed = 6f;

        [Tooltip("How close a fish must get to count as arrived. Must stay comfortably larger than " +
                 "the distance a fish covers in one physics step, or arrival can never be detected.")]
        [Min(0.01f)] [SerializeField] private float _arrivalDistance = 0.12f;

        [Header("Patrol")]
        [Tooltip("Ceiling on patrol speed. RasElBar shipped a 25 m/s override on every fish, which " +
                 "scattered the shoal across the whole area within seconds of the scene loading.")]
        [Min(0.1f)] [SerializeField] private float _maxPatrolSpeed = 2f;

        [Tooltip("How far from where it spawned a fish will wander.")]
        [Min(1f)] [SerializeField] private float _patrolRadius = 14f;

        [Header("Recovery")]
        [Tooltip("Seconds to wait before trying again when no fish was available to commission.")]
        [Min(0.5f)] [SerializeField] private float _retryInterval = 3f;

        [Tooltip("How long a dropped rod is left where it fell before it is returned to its stand.")]
        [Min(0.5f)] [SerializeField] private float _rodRecoverySeconds = 6f;

        [Tooltip("A rod further than this from its stand is out of play and gets returned.")]
        [Min(1f)] [SerializeField] private float _rodMaxDistance = 25f;

        public float MinBiteSeconds => Mathf.Min(_minBiteSeconds, _maxBiteSeconds);
        public float MaxBiteSeconds => Mathf.Max(_minBiteSeconds, _maxBiteSeconds);
        public float CruiseSpeed => _cruiseSpeed;
        public float MaxApproachSpeed => _maxApproachSpeed;
        public float ArrivalDistance => _arrivalDistance;
        public float MaxPatrolSpeed => _maxPatrolSpeed;
        public float PatrolRadius => _patrolRadius;
        public float RetryInterval => _retryInterval;
        public float RodRecoverySeconds => _rodRecoverySeconds;
        public float RodMaxDistance => _rodMaxDistance;

        private static FishingSettingsSO _cached;
        private static bool _lookedUp;

        /// <summary>
        /// The project's fishing settings, or null if the asset is missing. Cached, so the hundreds of
        /// components that ask for it cost one lookup between them.
        ///
        /// Returning null rather than a default instance is deliberate: a component that cannot find the
        /// asset must fall back on its own serialized values, which is what keeps scenes authored before
        /// this asset existed behaving exactly as they did.
        /// </summary>
        public static FishingSettingsSO Load()
        {
            if (_lookedUp) return _cached;

            _lookedUp = true;
            _cached = Resources.Load<FishingSettingsSO>(ResourcePath);

            if (_cached == null)
            {
                Debug.LogWarning($"No FishingSettings asset at Resources/{ResourcePath} — the fishing " +
                                 "system will use each component's own Inspector values instead.");
            }

            return _cached;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Swapped values are a natural typo and would otherwise produce a window that reads
            // backwards. Clamp rather than silently reorder, so the mistake stays visible in the asset.
            if (_maxBiteSeconds < _minBiteSeconds) _maxBiteSeconds = _minBiteSeconds;

            // Domain reload keeps the cache across an edit; drop it so a tweak takes effect in play mode.
            _cached = null;
            _lookedUp = false;
        }
#endif
    }
}
