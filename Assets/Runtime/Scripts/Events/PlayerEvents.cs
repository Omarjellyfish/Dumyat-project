using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The player has walked somewhere under their own power. Published by
    /// <see cref="PlayerActionProbe"/> once the rig has covered a short distance, not every frame —
    /// this answers "have they figured out the stick yet", so a nudge of the thumbstick should not
    /// count.
    /// </summary>
    public readonly struct PlayerMovedEvent : IGameEvent
    {
        /// <summary>Horizontal distance (m) covered since the probe last reported.</summary>
        public readonly float Distance;

        public PlayerMovedEvent(float distance)
        {
            Distance = distance;
        }
    }

    /// <summary>
    /// The player grabbed something with a hand. Published by <see cref="PlayerActionProbe"/> from the
    /// rig's own interactors, so it fires for ANY grabbable — unlike the per-object announcers such as
    /// <see cref="FishingRodPickedUpEvent"/>, which say what was picked up.
    /// </summary>
    public readonly struct PlayerGrabbedObjectEvent : IGameEvent
    {
        public readonly GameObject Object;

        public PlayerGrabbedObjectEvent(GameObject obj)
        {
            Object = obj;
        }
    }

    /// <summary>
    /// The player pulled the trigger on whatever they were holding. Fires for any activatable held
    /// object, which is what makes it useful for teaching the control rather than a specific tool.
    /// </summary>
    public readonly struct PlayerUsedHeldItemEvent : IGameEvent
    {
        public readonly GameObject Object;

        public PlayerUsedHeldItemEvent(GameObject obj)
        {
            Object = obj;
        }
    }

    /// <summary>Published by <see cref="PlayerPersistenceService"/> when a persistence key is unlocked for the first time.</summary>
    public readonly struct KeyUnlockedEvent : IGameEvent
    {
        public readonly string Key;

        public KeyUnlockedEvent(string key)
        {
            Key = key;
        }
    }
}
