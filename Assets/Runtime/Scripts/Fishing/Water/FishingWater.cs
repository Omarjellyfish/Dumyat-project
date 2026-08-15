using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The single answer to "is this water?" for the whole fishing system.
    ///
    /// It exists because there used to be two answers that never agreed. The rod settled its cast on a
    /// <em>layer</em> test (<c>Physics.CheckSphere(..., waterLayer)</c>) while the hook invited fish on a
    /// <em>tag</em> test (<c>CompareTag("Water")</c>) — and in Ras El Bar those resolve to different
    /// objects at different depths: the tagged trigger sits on layer 0, and the layer-4 colliders are
    /// untagged. A cast could therefore satisfy one gate and fail the other, so the line settled with no
    /// fish ever invited, or a fish was invited onto a line that could never be reeled. Both failures were
    /// silent and permanent.
    ///
    /// Accepting EITHER signal is deliberate. It means existing scenes keep working exactly as authored —
    /// nothing has to be re-tagged or moved to a layer — while the two systems can no longer disagree
    /// about what they are looking at.
    /// </summary>
    public static class FishingWater
    {
        /// <summary>The tag authored on water volumes. Kept public so callers never re-type the string.</summary>
        public const string WaterTag = "Water";

        // Reused across queries so a per-frame water test allocates nothing. Sized well past the number
        // of overlapping colliders any sane cast can produce; OverlapSphereNonAlloc truncates rather
        // than growing, and a truncated result only ever costs us a frame of detection latency.
        private static readonly Collider[] Overlap = new Collider[16];

        /// <summary>
        /// True when this collider is water — by tag or by layer. Use for trigger callbacks, where the
        /// collider is already in hand.
        /// </summary>
        public static bool IsWater(Collider collider, LayerMask waterLayers)
        {
            if (collider == null) return false;

            if (IsInMask(collider.gameObject.layer, waterLayers)) return true;

            // CompareTag throws if the tag is undefined in TagManager, which would take out the caller's
            // whole trigger callback. Every shipping scene defines "Water", but a stripped test scene
            // might not, and a fishing rod dropped into one should simply not fish.
            return collider.CompareTag(WaterTag);
        }

        /// <summary>
        /// True when this point is inside water. Tests real overlap rather than a layer-only sphere cast,
        /// so a tagged trigger volume counts even when it is not on the water layer.
        /// </summary>
        /// <param name="point">World position to test — normally the hook.</param>
        /// <param name="radius">Probe radius. Generous is better: this is sampled once per frame against
        /// a hook that can be travelling many metres per second, and a probe smaller than the per-frame
        /// step is a probe that gets skipped straight past.</param>
        public static bool IsPointInWater(Vector3 point, float radius, LayerMask waterLayers)
        {
            int count = Physics.OverlapSphereNonAlloc(
                point,
                radius,
                Overlap,
                ~0,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                if (IsWater(Overlap[i], waterLayers)) return true;
            }

            return false;
        }

        private static bool IsInMask(int layer, LayerMask mask)
        {
            return (mask.value & (1 << layer)) != 0;
        }
    }
}
