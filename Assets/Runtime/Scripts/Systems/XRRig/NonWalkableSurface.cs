using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Marks a collider the player must never end up standing on — water, chiefly.
    ///
    /// The water in these scenes is a solid plane with a collider on it, so walking off a quay does not
    /// drop the player into a void: it lands them ON the water, a metre or so down, where they can then
    /// stroll out across the Nile. That is not a fall in the physics sense — Ezbet's quay is only 1.2 m
    /// above the water — so no height-based check will ever catch it.
    ///
    /// Rather than guess at thresholds, the surface says so itself. <see cref="PlayerFallGuard"/> treats
    /// standing on one of these exactly as it treats falling out of the world: never records it as a
    /// safe spot, and returns the player to the last place they were legitimately standing.
    ///
    /// Deliberately a marker with no behaviour of its own, so it costs nothing and can be dropped onto
    /// any surface — water, a roof the player should not reach, a scenery mesh outside the play area.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Dumyat/Player/Non-Walkable Surface")]
    public class NonWalkableSurface : MonoBehaviour
    {
    }
}
