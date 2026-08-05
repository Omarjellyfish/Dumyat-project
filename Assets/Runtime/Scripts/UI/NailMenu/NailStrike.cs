using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// One hammer blow landing on a nail.
    ///
    /// Passed to every reaction component so each shapes its own response from the same numbers,
    /// rather than reaching back into <see cref="NailAction"/> for its private hit count. That is
    /// what lets the label ramp, the letters jolt and the dust burst all agree about how far in the
    /// nail is without any of them knowing about the others.
    /// </summary>
    public readonly struct NailStrike
    {
        /// <summary>Blows landed so far, including this one. Always at least 1.</summary>
        public readonly int Hits;

        /// <summary>Blows needed to drive this nail home.</summary>
        public readonly int HitsRequired;

        /// <summary>Relative speed of the impact, straight from the collision.</summary>
        public readonly float ImpactSpeed;

        /// <summary>World position of the nail head at the moment of the blow.</summary>
        public readonly Vector3 Point;

        public NailStrike(int hits, int hitsRequired, float impactSpeed, Vector3 point)
        {
            Hits = hits;
            HitsRequired = hitsRequired;
            ImpactSpeed = impactSpeed;
            Point = point;
        }

        /// <summary>0 before the first blow, 1 once the nail is flush with the bench.</summary>
        public float Progress => HitsRequired <= 0 ? 1f : Mathf.Clamp01((float)Hits / HitsRequired);

        /// <summary>True on the blow that sets the nail — the one that fires the menu action.</summary>
        public bool IsFinal => Hits >= HitsRequired;
    }
}
