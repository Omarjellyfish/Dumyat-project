using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The promise the fishing system makes to the player: once the line is settled in water, a fish
    /// WILL be on the hook within a bounded window. This class owns that promise and nothing else.
    ///
    /// It is a plain C# solver held as a field rather than a MonoBehaviour, for the same reason
    /// <see cref="LazyFollowYaw"/> is: it composes onto whatever already exists without forcing a prefab
    /// or scene change, and it can be unit-reasoned about without a running scene.
    ///
    /// The design turns the old model inside out. Before, the fish had a speed and the arrival time was
    /// whatever fell out of it — which is why an accidental 25 m/s scene override and a 5 mm arrival test
    /// could combine into "no fish, ever". Here the arrival TIME is the authored value and the speed is
    /// derived from it. That makes the swim speed an output, so a wrong speed in a scene can no longer
    /// break the contract, only the look of the approach.
    ///
    /// The fish is deliberately not commissioned the moment the line settles. If it were, a fish 5 m away
    /// with 60 s on the clock would have to creep at 8 cm/s to arrive on time, which reads as a dead fish.
    /// Instead it keeps patrolling until the latest moment it could still make the deadline at a normal
    /// cruising speed — so the approach always looks like a fish deciding to take the bait.
    /// </summary>
    public sealed class BiteSchedule
    {
        private float _biteAtTime;
        private bool _armed;

        /// <summary>True while a bite is promised and not yet delivered.</summary>
        public bool IsArmed => _armed;

        /// <summary>Absolute <see cref="Time.time"/> the fish is due to reach the hook.</summary>
        public float BiteAtTime => _biteAtTime;

        /// <summary>
        /// Start the clock. Rolls the actual bite moment once, uniformly inside the window, so every
        /// cast feels different but none of them can fall outside what the player was promised.
        /// </summary>
        public void Arm(float now, float minSeconds, float maxSeconds)
        {
            float min = Mathf.Max(0f, minSeconds);
            float max = Mathf.Max(min, maxSeconds);

            _biteAtTime = now + Random.Range(min, max);
            _armed = true;
        }

        /// <summary>
        /// Abandon the promise — the hook left the water, or the rod was put down. Deliberately separate
        /// from <see cref="Arm"/> so re-casting always starts a fresh, full window rather than inheriting
        /// whatever was left of the last one.
        /// </summary>
        public void Disarm()
        {
            _armed = false;
        }

        /// <summary>Seconds left before the fish is due. Never negative; zero means due now or overdue.</summary>
        public float SecondsUntilBite(float now)
        {
            return _armed ? Mathf.Max(0f, _biteAtTime - now) : 0f;
        }

        /// <summary>
        /// True once the deadline has passed. The caller's cue to stop asking nicely and put the fish on
        /// the hook — this is what makes the window a guarantee rather than a target.
        /// </summary>
        public bool IsOverdue(float now)
        {
            return _armed && now >= _biteAtTime;
        }

        /// <summary>
        /// Whether a fish this far away needs to set off now to arrive on time at a natural pace.
        ///
        /// Carries a deliberate lead margin. Comparing exactly against <c>distance / cruiseSpeed</c> puts
        /// the decision on a floating-point knife edge — <c>16f / 1.6f</c> is 9.99999985, not 10 — so the
        /// fish would be commissioned a frame or more late and have to make the time up by swimming
        /// unnaturally hard. Setting off slightly early costs nothing and keeps the approach unhurried.
        /// </summary>
        public bool ShouldCommission(float now, float distance, float cruiseSpeed)
        {
            if (!_armed) return false;
            if (cruiseSpeed <= 0f) return true;

            float travel = distance / cruiseSpeed;
            float lead = travel * 0.15f + 0.5f;

            return SecondsUntilBite(now) <= travel + lead;
        }

        /// <summary>
        /// The speed a committed fish must hold to cover the remaining distance and arrive exactly on the
        /// deadline. Recomputed every tick, so a fish that gets pushed off course or commissioned late
        /// simply swims harder — within limits — instead of quietly missing the window.
        /// </summary>
        public float SpeedFor(float now, float distance, float minSpeed, float maxSpeed)
        {
            float remaining = SecondsUntilBite(now);

            // Out of time: go as fast as we are allowed. The caller force-arrives shortly after.
            if (remaining <= 0.05f) return maxSpeed;

            return Mathf.Clamp(distance / remaining, minSpeed, maxSpeed);
        }
    }
}
