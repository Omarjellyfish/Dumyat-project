using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace DumyatRuntime
{
    /// <summary>
    /// Where a spoken line ends up in the mixer.
    ///
    /// Voice is the one channel that gets this wrong repeatedly, and it fails in two different ways
    /// that are indistinguishable in the Inspector:
    ///
    ///  * An AudioSource with NO <c>outputAudioMixerGroup</c> does not route through the mixer at all
    ///    — it goes straight to the listener, so it ignores every volume setting, Master included.
    ///    This is the default state of a freshly added AudioSource, so it is what every NPC prefab
    ///    drifts into.
    ///  * A source pointed at the general-purpose group obeys Master but silently ignores the Voice
    ///    slider, because that slider drives the mixer's separate 'Dialouge' group.
    ///
    /// Both live here rather than in each speaking system, so a new voice path cannot reintroduce
    /// them and the reasoning is written once. A caller that already declares a group is always left
    /// alone — a designer can still send a specific character somewhere else on purpose.
    /// </summary>
    public static class VoiceMixerRouting
    {
        /// <summary>
        /// Things already warned about, so a mis-authored NPC logs once rather than once per spoken
        /// line. Keyed on instance ID because the AudioSource is destroyed with its scene.
        /// </summary>
        private static readonly HashSet<int> Warned = new HashSet<int>();

        /// <summary>
        /// Routes <paramref name="source"/> through <paramref name="voiceGroup"/> if it has no group
        /// of its own. For systems that play voice on an AudioSource they do not own — an NPC's
        /// lipsync source, a TTS target — where the source's wiring is a prefab detail no code sees.
        /// </summary>
        public static void EnsureRouted(AudioSource source, AudioMixerGroup voiceGroup, UnityEngine.Object owner)
        {
            if (source == null || source.outputAudioMixerGroup != null) return;

            if (voiceGroup != null)
            {
                source.outputAudioMixerGroup = voiceGroup;
                return;
            }

            // Nothing to route it to: say so rather than leave the voice outside the mixer, which is
            // exactly the failure this class exists to stop.
            Warn(source.GetInstanceID(), owner,
                 $"'{source.name}' has no output mixer group and no voice group was supplied, so it " +
                 "bypasses the mixer entirely and will ignore every volume setting — Master included. " +
                 "Assign the MainMixer 'Dialouge' group on the AudioSource, or on the component " +
                 "playing through it.");
        }

        /// <summary>
        /// Picks the group a spoken line should play on, for systems that assign the group per call
        /// (a pooled AudioSource, say). <paramref name="requested"/> wins when the caller named one;
        /// otherwise <paramref name="voiceGroup"/>; <paramref name="fallbackGroup"/> is the
        /// last resort and is reported, because landing there means the Voice slider does nothing.
        /// </summary>
        public static AudioMixerGroup ResolveGroup(AudioMixerGroup requested, AudioMixerGroup voiceGroup,
                                                   AudioMixerGroup fallbackGroup, UnityEngine.Object owner)
        {
            if (requested != null) return requested;
            if (voiceGroup != null) return voiceGroup;

            Warn(owner != null ? owner.GetInstanceID() : 0, owner,
                 "no voice mixer group is assigned, so spoken lines fall back to the general-purpose " +
                 "group and follow that channel's volume instead of the Voice slider. Assign the " +
                 "MainMixer 'Dialouge' group.");

            return fallbackGroup;
        }

        private static void Warn(int key, UnityEngine.Object owner, string message)
        {
            if (!Warned.Add(key)) return;

            Debug.LogWarning($"{nameof(VoiceMixerRouting)}: {message}", owner);
        }
    }
}
