using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Contract for an NPC that can perform narrative actions.
    /// Implemented by <see cref="NarrativeCharacterView"/> on NPC prefabs.
    /// </summary>
    public interface INpcActor
    {
        /// <summary>Plays the clip on the NPC's own AudioSource (lipsync-ready).</summary>
        void PlayVoiceLine(AudioClip clip, float volume = 1f);

        /// <summary>Stops any currently playing voice line on this NPC.</summary>
        void StopVoiceLine();
    }
}
