using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Shared rules for putting the player rig somewhere.
    ///
    /// There are two paths that place the rig — <see cref="PlayerRigSpawner"/> spawns one into a
    /// scene that authors none, and <see cref="FadeTransitionHandler"/> carries the existing one
    /// across a bus journey — and both read the same ArrivalPoint markers. Anything that must be true
    /// of a spawn belongs here rather than in whichever of the two happened to need it first.
    /// </summary>
    public static class PlayerSpawnPose
    {
        /// <summary>
        /// The heading of <paramref name="rotation"/> with its pitch and roll thrown away.
        ///
        /// An ArrivalPoint is placed by hand, and markers get nudged: carpentar_room's sat at 4.26°
        /// of pitch and El_manara's second one at 4.60°, neither of which is visible in the Inspector
        /// because their Euler hints still read (0, 0, 0). Copying that straight onto the rig tilted
        /// the player's whole world — the horizon came in on a slant, which in a headset is not a
        /// cosmetic bug but a route to motion sickness, and it is not something the player can correct
        /// by turning their head.
        ///
        /// A standing human is upright. The only part of a spawn marker's rotation that means
        /// anything to a rig is which way it faces, so that is the only part taken.
        /// </summary>
        public static Quaternion UprightFacing(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0f;

            // A marker pointing straight up or down has no heading to preserve. Falling back to
            // identity beats LookRotation's zero-vector warning and an undefined facing.
            if (forward.sqrMagnitude < 1e-6f) return Quaternion.identity;

            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        /// <summary>Convenience overload for the common "I have the marker's transform" case.</summary>
        public static Quaternion UprightFacing(Transform marker) =>
            marker == null ? Quaternion.identity : UprightFacing(marker.rotation);
    }
}
