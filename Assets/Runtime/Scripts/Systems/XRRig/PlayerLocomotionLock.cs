using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace DumyatRuntime
{
    /// <summary>
    /// Stops the player travelling, without stopping them looking around.
    ///
    /// It replaces a lock that disabled one GameObject found by the "Move" tag. That was two problems at
    /// once. RasElBar authors no rig — it is spawned at runtime — so during a scene where the rig had not
    /// appeared yet the tag lookup found nothing and the lock silently did nothing at all. And even when
    /// it worked it only ever switched off walking, while the rig it spawns also carries a Teleportation
    /// provider, two teleport interactors and a Grab Move provider. The player could stand on the boat
    /// and simply teleport off it.
    ///
    /// Locking by <see cref="LocomotionProvider"/> instead of by tag catches every way the rig can move,
    /// including any added later, and needs no scene wiring to keep working.
    ///
    /// Turning is deliberately left alone. Taking it away in VR causes nausea without preventing any
    /// actual movement, and standing still while being able to look around is the whole point of fishing
    /// from a small boat.
    /// </summary>
    public static class PlayerLocomotionLock
    {
        // Remembered so unlocking restores exactly what was switched off, and never enables a provider
        // that was already disabled for its own reasons.
        private static readonly List<LocomotionProvider> Suppressed = new();

        /// <summary>True while the player is currently pinned in place.</summary>
        public static bool IsLocked { get; private set; }

        /// <summary>
        /// Pins the player, or releases them. Safe to call repeatedly and safe to call with no rig in
        /// the scene — a lock applied before the rig exists is simply re-applied on the next call.
        /// </summary>
        public static void SetLocked(bool locked)
        {
            if (locked) Apply();
            else Release();
        }

        private static void Apply()
        {
            Release();

            Transform rig = PlayerRigTeleport.FindRig();

            if (rig == null)
            {
                Debug.LogWarning("PlayerLocomotionLock: no player rig found, so movement could not be " +
                                 "locked. Is the rig spawned yet?");
                return;
            }

            // Include inactive: a provider on a disabled object is not a reason to miss it, and it must
            // still be recorded so unlocking does not switch something on that was off to begin with.
            LocomotionProvider[] providers = rig.GetComponentsInChildren<LocomotionProvider>(true);

            foreach (LocomotionProvider provider in providers)
            {
                if (provider == null) continue;
                if (!provider.enabled) continue;
                if (IsExempt(provider)) continue;

                provider.enabled = false;
                Suppressed.Add(provider);
            }

            IsLocked = true;

            if (Suppressed.Count == 0)
            {
                Debug.LogWarning("PlayerLocomotionLock: found the rig but no locomotion providers to " +
                                 "lock. The player may still be able to move.", rig);
            }
        }

        private static void Release()
        {
            foreach (LocomotionProvider provider in Suppressed)
            {
                // Destroyed with the rig on a scene change — Unity's fake null makes this safe to test.
                if (provider != null) provider.enabled = true;
            }

            Suppressed.Clear();
            IsLocked = false;
        }

        /// <summary>
        /// Providers that must keep running even while the player is pinned.
        ///
        /// <b>Turning</b> — taking it away in VR causes nausea without preventing any actual movement.
        /// <b>Gravity</b> — it is not locomotion at all, it is what keeps the player's body on the deck.
        /// Switching it off left them hovering rather than standing, which is a worse bug than the one
        /// the lock is here to fix, and it is only caught by testing against a real rig.
        ///
        /// Matched on the type name so this covers the current SnapTurnProvider/ContinuousTurnProvider,
        /// their deprecated Base counterparts and any project-specific provider added later, without
        /// this file taking a hard reference on deprecated XRI types. Note this matches a C# type name —
        /// it is not interpreting anything the player did.
        /// </summary>
        private static bool IsExempt(LocomotionProvider provider)
        {
            string typeName = provider.GetType().Name;

            return typeName.IndexOf("Turn", System.StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Gravity", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
