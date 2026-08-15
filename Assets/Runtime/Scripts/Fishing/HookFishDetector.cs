using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Decides when a fish takes this hook, and guarantees it happens.
    ///
    /// The old version asked exactly one fish, exactly once, the instant the hook touched water — and
    /// then latched, even when it had found no fish to ask. A failed bite roll, an empty shoal, a fish
    /// that was already busy, or simply landing in a quiet corner all produced the same outcome: nothing,
    /// forever, with no retry and nothing logged. That is the shape of "I waited and no fish came".
    ///
    /// Now a settled line is a promise. <see cref="BiteSchedule"/> rolls a bite moment inside the
    /// authored window, this component keeps trying until it can deliver on it, and if the deadline
    /// arrives with a fish still en route it puts that fish on the hook. Every failure along the way is
    /// recoverable and every one of them says so in the log.
    /// </summary>
    public class HookFishDetector : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Auto-resolved from the scene if left empty.")]
        public FishArea fishArea;

        [SerializeField] private HookingFish hookingFish;

        [Header("Detection")]
        public float fishDetectionRadius = 8f;

        [Tooltip("Layers that count as water, in addition to anything tagged 'Water'. Accepting both is " +
                 "what stops the rod and the hook disagreeing about where the water is.")]
        [SerializeField] private LayerMask _waterLayers = 1 << 4;

        [Header("Bite Window")]
        [Tooltip("Optional. Leave empty to use the project's FishingSettings asset from Resources; the " +
                 "values below are only the fallback for a scene that has neither.")]
        [SerializeField] private FishingSettingsSO _settings;

        [Tooltip("Fallback: earliest a fish may reach the hook after the line settles, in seconds.")]
        [SerializeField] private float _minBiteSeconds = 15f;

        [Tooltip("Fallback: latest a fish may reach the hook after the line settles, in seconds. This " +
                 "is a hard guarantee: if nothing has arrived by now, a fish is placed on the hook.")]
        [SerializeField] private float _maxBiteSeconds = 45f;

        [Tooltip("Fallback: the pace a fish sets off at. The schedule speeds it up if it is late.")]
        [SerializeField] private float _cruiseSpeed = 1.6f;

        [Tooltip("Fallback: seconds to wait before looking again when no fish was available. Keeps a " +
                 "shoal that is temporarily busy or out of range from ending the session.")]
        [SerializeField] private float _retryInterval = 3f;

        private readonly BiteSchedule _schedule = new();
        private readonly HashSet<Collider> _waterContacts = new();

        private FishBehaviour _committedFish;
        private float _nextRetryTime;
        private bool _isInWater;

        /// <summary>Seconds until a fish is due, or 0 when the line is not fishing.</summary>
        public float SecondsUntilBite => _schedule.SecondsUntilBite(Time.time);

        /// <summary>True while the line is settled in water and a bite is promised.</summary>
        public bool IsFishing => _isInWater && _schedule.IsArmed;

        // Every tuning value resolves through the shared asset when there is one, so the bite window is
        // edited in a single place rather than on each rod in each scene.
        private float MinBite => _settings != null ? _settings.MinBiteSeconds : _minBiteSeconds;
        private float MaxBite => _settings != null ? _settings.MaxBiteSeconds : _maxBiteSeconds;
        private float Cruise => _settings != null ? _settings.CruiseSpeed : _cruiseSpeed;
        private float Retry => _settings != null ? _settings.RetryInterval : _retryInterval;
        private float TopSpeed => _settings != null ? _settings.MaxApproachSpeed : _cruiseSpeed * 4f;

        private void Awake()
        {
            if (_settings == null) _settings = FishingSettingsSO.Load();

            // The hooking trigger lives on a CHILD of this object on the Rod prefab, so the original
            // GetComponent here always resolved null and the hook silently lost its re-arm.
            if (hookingFish == null) hookingFish = GetComponentInChildren<HookingFish>();
            if (hookingFish == null) hookingFish = GetComponentInParent<HookingFish>();

            if (fishArea == null) fishArea = FindFirstObjectByType<FishArea>();

            if (fishArea == null)
            {
                // Was an unguarded dereference. The Rod PREFAB ships this field null and only RasElBar
                // patches it by override, so any other scene — or a rod spawned at runtime — threw here
                // on the first cast and killed fishing for the session with no message.
                Debug.LogWarning($"'{name}' found no FishArea in the scene — this rod cannot catch fish. " +
                                 "Add a FishArea to the water, or assign one on this component.", this);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!FishingWater.IsWater(other, _waterLayers)) return;

            _waterContacts.Add(other);

            if (_isInWater) return;

            _isInWater = true;
            BeginFishing();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!_waterContacts.Remove(other)) return;
            if (_waterContacts.Count > 0) return;

            _isInWater = false;
            StopFishing();
        }

        private void BeginFishing()
        {
            // A new attempt: let the hook accept a fresh catch. Refuses harmlessly if a fish is still
            // genuinely attached.
            hookingFish?.ResetHook();

            _schedule.Arm(Time.time, MinBite, MaxBite);
            _nextRetryTime = 0f;

            Debug.Log($"Line settled — a fish is due in {_schedule.SecondsUntilBite(Time.time):0.#}s.", this);
        }

        private void StopFishing()
        {
            _schedule.Disarm();
            ReleaseCommitment();
        }

        private void ReleaseCommitment()
        {
            if (_committedFish == null) return;

            _committedFish.CancelHookInvestigation();
            _committedFish = null;
        }

        private void Update()
        {
            if (!_isInWater || !_schedule.IsArmed) return;
            if (hookingFish != null && hookingFish.HasFish) return;

            // The committed fish may have been caught by something else, destroyed, or pushed out of the
            // area mid-approach. Any of those used to end the attempt silently; now we just pick another.
            if (_committedFish != null &&
                (_committedFish.IsHooked || !_committedFish.IsInsideFishArea))
            {
                _committedFish = null;
            }

            if (_committedFish == null)
            {
                TryCommitFish();
                return;
            }

            float distance = Vector3.Distance(_committedFish.transform.position, transform.position);
            _committedFish.SetApproachSpeed(_schedule.SpeedFor(Time.time, distance, 0.4f, TopSpeed));

            // The promise falls due. Whatever the fish is doing, it is on the hook now.
            if (_schedule.IsOverdue(Time.time))
            {
                _committedFish.ForceArrive();
            }
        }

        private void TryCommitFish()
        {
            if (fishArea == null) return;
            if (Time.time < _nextRetryTime) return;

            FishBehaviour candidate = fishArea.GetNearestAvailableFish(transform.position, fishDetectionRadius);

            if (candidate == null)
            {
                _nextRetryTime = Time.time + Retry;

                // Push the deadline out rather than silently missing it. The player waits a little
                // longer, which is far better than the old behaviour of waiting forever.
                if (_schedule.IsOverdue(Time.time))
                {
                    Debug.LogWarning(
                        $"'{name}' is overdue for a bite but no fish is available within " +
                        $"{fishDetectionRadius}m (area has {fishArea.AvailableFishCount} in range of it). " +
                        "Retrying — check the shoal has not drifted out of the FishArea.", this);

                    _schedule.Arm(Time.time, Retry, Retry * 2f);
                }

                return;
            }

            float distance = Vector3.Distance(candidate.transform.position, transform.position);

            // Leave the fish patrolling until it actually needs to set off. Commissioning it immediately
            // would have it creep across the gap at a few centimetres a second to burn the clock, which
            // reads as a broken fish rather than a cautious one.
            if (!_schedule.ShouldCommission(Time.time, distance, Cruise)) return;

            float speed = _schedule.SpeedFor(Time.time, distance, 0.4f, TopSpeed);

            if (candidate.BeginApproach(transform, speed, OnFishArrived))
            {
                _committedFish = candidate;
            }
        }

        private void OnFishArrived(FishBehaviour fish)
        {
            if (fish == null) return;

            Fish caught = fish.GetComponent<Fish>();

            if (hookingFish != null && hookingFish.TryAttach(caught))
            {
                _committedFish = null;
                _schedule.Disarm();
                return;
            }

            // Attach refused — the hook is occupied, or this fish has no Rigidbody. Hand the fish back
            // to the shoal rather than leaving it flagged as hooked with no joint, which is precisely how
            // fish used to disappear from the game one at a time.
            Debug.LogWarning($"'{fish.name}' reached the hook but could not be attached. " +
                             "Returning it to the water.", this);

            fish.ReleaseFromHook();
            _committedFish = null;
            _nextRetryTime = Time.time + Retry;
        }
    }
}
