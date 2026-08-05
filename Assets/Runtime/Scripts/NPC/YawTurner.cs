using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Eases a character's visual body around its Y axis to face a world point, staying upright.
    ///
    /// A plain solver, not a component — the same shape as <see cref="CameraBillboard"/> and
    /// <see cref="AnimatorStateSwitcher"/>. <see cref="MerchantApproachMover"/> and
    /// <see cref="MerchantProximityTrigger"/> both turn a merchant toward the player and previously
    /// each did the yaw-flattening and look-at themselves.
    ///
    /// The ease is exponential rather than linear: it starts quickly and settles gently, which reads as
    /// a person turning rather than a turret tracking. <c>1 - e^(-k·dt)</c> makes that frame-rate
    /// independent, so the turn feels identical at 72 Hz in a headset and at 200 fps in the editor — a
    /// plain <c>Slerp(a, b, k·dt)</c> would turn faster the better the frame rate. A degrees-per-second
    /// cap keeps a large angle from whipping round on the first frame.
    /// </summary>
    public sealed class YawTurner
    {
        /// <summary>Within this many degrees of the target counts as facing it.</summary>
        public const float AlignedDegrees = 0.5f;

        /// <summary>
        /// Eases <paramref name="body"/> one step toward facing <paramref name="worldPoint"/>.
        /// Returns true once it is there (or when there is nothing to do).
        /// </summary>
        public bool Step(Transform body, Vector3 worldPoint, float responsiveness,
                         float maxDegreesPerSecond, float deltaTime)
        {
            if (body == null) return true;
            if (!TryFlatLookAt(body, worldPoint, out Quaternion target)) return true;
            return StepTo(body, target, responsiveness, maxDegreesPerSecond, deltaTime);
        }

        /// <summary>
        /// Eases <paramref name="body"/> one step toward an explicit rotation — used to settle back to a
        /// remembered home rotation. Returns true once it is there.
        /// </summary>
        public bool StepTo(Transform body, Quaternion target, float responsiveness,
                           float maxDegreesPerSecond, float deltaTime)
        {
            if (body == null) return true;
            if (Quaternion.Angle(body.rotation, target) <= AlignedDegrees)
            {
                body.rotation = target;   // settle exactly, so it stops drifting a fraction of a degree
                return true;
            }

            float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, responsiveness) * deltaTime);
            Quaternion eased = Quaternion.Slerp(body.rotation, target, t);

            // Cap the step so a merchant who needs to turn right round does it at a believable rate
            // instead of snapping on the first frame of the ease.
            body.rotation = Quaternion.RotateTowards(body.rotation, eased, maxDegreesPerSecond * deltaTime);
            return false;
        }

        /// <summary>Turns instantly — for placing a character, not for anything the player watches.</summary>
        public static void Snap(Transform body, Vector3 worldPoint)
        {
            if (body == null) return;
            if (TryFlatLookAt(body, worldPoint, out Quaternion target)) body.rotation = target;
        }

        /// <summary>
        /// The upright rotation that faces <paramref name="worldPoint"/>. False when the point is
        /// directly above or below the body, where the flattened direction has no yaw to speak of.
        /// </summary>
        public static bool TryFlatLookAt(Transform body, Vector3 worldPoint, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (body == null) return false;

            Vector3 flat = worldPoint - body.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) return false;

            rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            return true;
        }

        /// <summary>The upright rotation for a travel direction, ignoring any vertical component.</summary>
        public static bool TryFlatLookDirection(Vector3 direction, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return false;

            rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            return true;
        }
    }
}
