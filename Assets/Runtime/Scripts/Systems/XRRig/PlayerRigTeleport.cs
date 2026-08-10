using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Moving the player rig somewhere, correctly.
    ///
    /// Two things fight a naive <c>transform.position = x</c> on an XR rig, and both are silent:
    /// a <see cref="CharacterController"/> writes the transform straight back from its own internal
    /// position unless it is switched off across the change, and a <see cref="Rigidbody"/> keeps
    /// whatever velocity it had, so a player who fell arrives already plummeting and a player who
    /// walked arrives still walking.
    ///
    /// That knowledge lived inside <see cref="PlayerFallGuard"/> until <see cref="BoatTransit"/>
    /// needed the same move. It lives here now, once, and both call it — a second copy of this would
    /// be a bug that only shows up on one of the two teleports.
    /// </summary>
    public static class PlayerRigTeleport
    {
        /// <summary>
        /// The player rig, or null. Rigs are per-scene here — some scenes author their own, others
        /// have one spawned by <see cref="PlayerRigSpawner"/> after the scene load completes — so this
        /// must be called when the rig is needed, never cached at <c>Awake</c> or <c>Start</c>.
        /// </summary>
        public static Transform FindRig()
        {
            GameObject tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null) return tagged.transform;

            return Camera.main != null ? Camera.main.transform.root : null;
        }

        /// <summary>Moves the rig to a world position, keeping its current facing.</summary>
        public static void MoveTo(Transform rig, Vector3 position)
        {
            if (rig == null) return;
            Apply(rig, position, rig.rotation);
        }

        /// <summary>
        /// Moves the rig to a marker, taking only the marker's heading.
        ///
        /// Only the heading, because markers are placed by hand and pick up a degree or two of pitch
        /// that the Inspector does not show — copied onto a rig it brings the horizon in on a slant,
        /// which in a headset is a route to motion sickness rather than a cosmetic bug, and is not
        /// something the player can correct by turning their head. See <see cref="PlayerSpawnPose"/>.
        /// </summary>
        public static void MoveTo(Transform rig, Transform marker)
        {
            if (rig == null || marker == null) return;
            Apply(rig, marker.position, PlayerSpawnPose.UprightFacing(marker));
        }

        private static void Apply(Transform rig, Vector3 position, Quaternion rotation)
        {
            CharacterController controller = rig.GetComponentInChildren<CharacterController>();
            bool hadController = controller != null && controller.enabled;
            if (hadController) controller.enabled = false;

            rig.SetPositionAndRotation(position, rotation);

            Rigidbody body = rig.GetComponentInChildren<Rigidbody>();
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            if (hadController) controller.enabled = true;
        }
    }
}
