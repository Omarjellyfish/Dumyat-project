using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Reusable "lazy follow" solver for head-anchored world-space UI, shared by
    /// <see cref="NotificationHudManager"/> and <see cref="LazyFollowAnchor"/>.
    ///
    /// The panel is anchored to a WORLD yaw rather than to the camera, so small head turns slide it
    /// across the view instead of dragging it around — which is what makes VR UI feel stable rather
    /// than glued to your face. Only once the panel would leave a comfort cone (on either side) does
    /// the anchor ease after the head, smoothed so it never snaps.
    ///
    /// Plain C# — no MonoBehaviour, no Unity lifecycle. Hold one per panel and call
    /// <see cref="Solve"/> from LateUpdate.
    /// </summary>
    public sealed class LazyFollowYaw
    {
        private float _yaw;              // world yaw (deg) the panel is horizontally anchored to
        private float _yawVelocity;      // SmoothDampAngle velocity ref
        private bool _needsReanchor = true;

        /// <summary>The current anchored yaw in degrees. Valid after the first <see cref="Solve"/>.</summary>
        public float Yaw => _yaw;

        /// <summary>
        /// Snap the anchor to the camera's yaw on the next <see cref="Solve"/> instead of easing to it.
        /// Call when a panel first appears, so it arrives in front of the player rather than drifting in.
        /// </summary>
        public void Reanchor() => _needsReanchor = true;

        /// <summary>
        /// Advances the anchor and returns the yaw (deg) the panel should be placed at.
        /// </summary>
        /// <param name="cameraYaw">Current camera yaw in degrees (<c>cam.eulerAngles.y</c>).</param>
        /// <param name="panelYawOffset">
        /// Yaw offset (deg) from the anchor to the panel's VISUAL CENTRE. Pass 0 for a panel centred on
        /// the forward ray; pass <c>Atan2(rightOffset, distance) * Rad2Deg</c> for one placed off to the side.
        /// Clamping the centre rather than the anchor is what keeps looking left and right symmetric.
        /// </param>
        /// <param name="deadzoneDeg">Half-angle of the comfort cone. Inside it the panel does not follow.</param>
        /// <param name="smoothTime">Smoothing time (s) for the re-centre. Lower is snappier, higher is lazier.</param>
        /// <param name="deltaTime">Frame delta — use <c>Time.unscaledDeltaTime</c> so a paused game still eases.</param>
        public float Solve(float cameraYaw, float panelYawOffset, float deadzoneDeg, float smoothTime, float deltaTime)
        {
            if (_needsReanchor)
            {
                _yaw = cameraYaw;
                _yawVelocity = 0f;
                _needsReanchor = false;
                return _yaw;
            }

            float panelInView = Mathf.DeltaAngle(cameraYaw, _yaw + panelYawOffset);
            if (Mathf.Abs(panelInView) > deadzoneDeg)
            {
                float targetYaw = cameraYaw + Mathf.Sign(panelInView) * deadzoneDeg - panelYawOffset;
                _yaw = Mathf.SmoothDampAngle(_yaw, targetYaw, ref _yawVelocity,
                                             smoothTime, Mathf.Infinity, deltaTime);
            }

            return _yaw;
        }
    }
}
