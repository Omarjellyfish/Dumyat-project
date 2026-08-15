using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The volume of water fish live in, and the register of which fish are currently in it.
    ///
    /// The register used to be one-way. <c>RegisterFish</c> was called from exactly one place — a fish's
    /// <c>Start</c> — while the periodic bounds sweep removed entries permanently, and the trigger
    /// callback set a fish's "inside" flag without ever putting it back on the list. So every fish that
    /// drifted out of bounds once, or was caught and lifted out, left the shoal for the rest of the
    /// scene. With thirty fish, no spawner and no replenishment, fishing wound down toward a state where
    /// no fish existed to be found and nothing said so. Membership is now derived from the bounds on
    /// every sweep, in both directions.
    /// </summary>
    public class FishArea : MonoBehaviour
    {
        private readonly List<FishBehaviour> _fishes = new();

        // Every fish that has ever registered, whether or not it is currently inside the bounds. This is
        // what lets a fish rejoin: the sweep can only re-admit fish it still knows about.
        private readonly List<FishBehaviour> _known = new();

        private BoxCollider _areaCollider;

        [Tooltip("Legacy field: the Rigidbody of the fish most recently selected for a hook. Kept so " +
                 "existing scene references still resolve, but nothing reads it any more — it was a " +
                 "single shared slot, so two casts in quick succession made one fish write gravity " +
                 "state onto another.")]
        public Rigidbody hookedRb;

        [Header("Leave Detection")]
        [Tooltip("How often (in seconds) to re-check which fish are inside the area bounds.")]
        public float leaveCheckInterval = 0.5f;

        private float _leaveCheckTimer;

        /// <summary>How many fish are currently inside the area and available to take a hook.</summary>
        public int AvailableFishCount => _fishes.Count;

        private void Awake()
        {
            _areaCollider = GetComponent<BoxCollider>();

            if (_areaCollider == null)
            {
                Debug.LogWarning($"FishArea '{name}' has no BoxCollider — it cannot track fish. " +
                                 "Add one and mark it as a trigger.", this);
            }
        }

        private void Update()
        {
            _leaveCheckTimer += Time.deltaTime;

            if (_leaveCheckTimer >= leaveCheckInterval)
            {
                _leaveCheckTimer = 0f;
                RefreshMembership();
            }
        }

        /// <summary>
        /// Re-derive membership from the bounds. Fish that have left are dropped from the active list;
        /// fish that have come back are re-admitted. Both directions matter — only doing the first is
        /// what made the shoal drain away.
        /// </summary>
        private void RefreshMembership()
        {
            if (_areaCollider == null) return;

            Bounds bounds = _areaCollider.bounds;

            for (int i = _known.Count - 1; i >= 0; i--)
            {
                FishBehaviour fish = _known[i];

                if (fish == null)
                {
                    _known.RemoveAt(i);
                    _fishes.Remove(null);
                    continue;
                }

                bool inside = bounds.Contains(fish.transform.position);
                bool tracked = _fishes.Contains(fish);

                if (inside && !tracked)
                {
                    _fishes.Add(fish);
                    fish.SetInsideFishArea(true);
                }
                else if (!inside && tracked)
                {
                    _fishes.Remove(fish);
                    fish.SetInsideFishArea(false);
                }
            }
        }

        public void RegisterFish(FishBehaviour fish)
        {
            if (fish == null) return;

            if (!_known.Contains(fish)) _known.Add(fish);
            if (!_fishes.Contains(fish)) _fishes.Add(fish);
        }

        public void UnregisterFish(FishBehaviour fish)
        {
            _fishes.Remove(fish);
            _known.Remove(fish);
        }

        public Bounds GetBounds()
        {
            return _areaCollider != null ? _areaCollider.bounds : new Bounds(transform.position, Vector3.one);
        }

        /// <summary>True when this world point lies inside the area.</summary>
        public bool Contains(Vector3 worldPoint)
        {
            return _areaCollider != null && _areaCollider.bounds.Contains(worldPoint);
        }

        /// <summary>
        /// The nearest fish that could actually take a hook right now — inside the area, not already
        /// hooked, and not already committed to another hook. The old version returned the nearest fish
        /// of any state, so a fish that was already on its way somewhere else could be handed out again.
        /// </summary>
        public FishBehaviour GetNearestAvailableFish(Vector3 hookPosition, float maxDistance)
        {
            FishBehaviour nearest = null;
            float nearestDistance = maxDistance;

            foreach (FishBehaviour fish in _fishes)
            {
                if (fish == null) continue;
                if (fish.IsHooked) continue;
                if (fish.IsCommitted) continue;

                float distance = Vector3.Distance(fish.transform.position, hookPosition);

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = fish;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Legacy overload. Kept because it was public API, but it no longer writes
        /// <see cref="hookedRb"/> — that shared slot was a cross-talk bug, not a feature.
        /// </summary>
        public FishBehaviour GetNearestFish(Vector3 hookPosition, float maxDistance)
        {
            return GetNearestAvailableFish(hookPosition, maxDistance);
        }

        private void OnTriggerEnter(Collider other)
        {
            FishBehaviour fish = ResolveFish(other);
            if (fish == null) return;

            RegisterFish(fish);
            fish.SetInsideFishArea(true);
        }

        private void OnTriggerExit(Collider other)
        {
            FishBehaviour fish = ResolveFish(other);
            if (fish == null) return;

            _fishes.Remove(fish);
            fish.SetInsideFishArea(false);
        }

        // Fish colliders sit on child objects on some prefabs, so a plain GetComponent misses them.
        private static FishBehaviour ResolveFish(Collider other)
        {
            if (other == null) return null;
            if (other.TryGetComponent(out FishBehaviour fish)) return fish;

            Rigidbody body = other.attachedRigidbody;
            return body != null ? body.GetComponent<FishBehaviour>() : null;
        }

        public Vector3 GetRandomPointInsideArea()
        {
            if (_areaCollider == null) return transform.position;

            Vector3 halfSize = _areaCollider.size * 0.5f;

            Vector3 localPoint = _areaCollider.center + new Vector3(
                Random.Range(-halfSize.x, halfSize.x),
                Random.Range(-halfSize.y, halfSize.y),
                Random.Range(-halfSize.z, halfSize.z));

            return _areaCollider.transform.TransformPoint(localPoint);
        }

        /// <summary>
        /// A random point within <paramref name="radius"/> of <paramref name="origin"/>, kept inside the
        /// area. Fish patrol around where they were placed rather than anywhere in the box — in RasElBar
        /// that box is 672 x 649 m, so drawing uniformly from it scattered the shoal far out to sea
        /// within seconds of the scene loading, long before the player finished the intro and cast.
        /// </summary>
        public Vector3 GetRandomPointNear(Vector3 origin, float radius)
        {
            Vector3 candidate = origin + Random.insideUnitSphere * radius;

            if (_areaCollider == null) return candidate;

            return _areaCollider.ClosestPoint(candidate);
        }
    }
}
