using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>How a billboard turns to meet the player.</summary>
    public enum BillboardMode
    {
        /// <summary>
        /// Face the camera squarely, pitching up or down to meet a head above or below it. Correct for
        /// a panel the player reads from close range, where a flat panel would foreshorten badly.
        /// </summary>
        Upright,

        /// <summary>
        /// Turn on the Y axis only, staying perfectly vertical wherever the head is. Correct for a
        /// panel anchored in the world that should read as a standing sign rather than a held card.
        /// </summary>
        YawOnly
    }

    /// <summary>
    /// Turns a world-space transform to face the player's camera. A plain solver, not a component —
    /// the same shape as <see cref="LazyFollowYaw"/>, so a MonoBehaviour holds one as a field and calls
    /// <see cref="Apply"/> from its own LateUpdate instead of re-implementing the maths.
    ///
    /// Both jobs live here so neither can be got wrong in isolation:
    ///
    /// 1. THE FACING. Forward points AWAY from the camera, which is what makes a Canvas or a TMP quad
    ///    readable rather than mirrored, and <c>Vector3.up</c> is passed explicitly so the panel never
    ///    rolls with the player's head — a rolled caption is unreadable in a headset.
    ///
    /// 2. THE CAMERA. Re-resolved whenever the cached one has gone, rather than cached once at startup.
    ///    <see cref="Camera.main"/> is null for a frame or two mid scene-load, while the outgoing
    ///    scene's camera is destroyed before the incoming rig registers (see <see cref="ScreenBlinder"/>,
    ///    which hit exactly this), and the carried-over XR rig destroys any duplicate camera the new
    ///    scene supplies. A component that caches once is then left holding a destroyed camera that
    ///    never comes back, silently stops billboarding, and freezes at whatever rotation it was
    ///    authored with — which reads in-game as the UI facing the wrong way.
    /// </summary>
    public sealed class CameraBillboard
    {
        private Camera _camera;

        /// <summary>
        /// Points <paramref name="target"/> at the player. Returns false — leaving the transform
        /// untouched — when there is no camera yet, so a caller can tell "not placed" from "placed".
        /// </summary>
        public bool Apply(Transform target, BillboardMode mode = BillboardMode.Upright)
        {
            if (target == null || !TryResolveCamera(out Camera camera)) return false;

            Vector3 away = target.position - camera.transform.position;
            if (mode == BillboardMode.YawOnly) away.y = 0f;

            // Degenerate when the head is exactly on the panel (or exactly above it, once flattened):
            // LookRotation would spit out a warning and an arbitrary rotation, so hold the last one.
            if (away.sqrMagnitude < 1e-6f) return false;

            target.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            return true;
        }

        /// <summary>The player's camera, re-resolving if the cached one has been destroyed.</summary>
        public bool TryResolveCamera(out Camera camera)
        {
            if (_camera == null) _camera = Camera.main;
            camera = _camera;
            return camera != null;
        }

        /// <summary>Drops the cached camera so the next call resolves afresh.</summary>
        public void Invalidate() => _camera = null;
    }
}
