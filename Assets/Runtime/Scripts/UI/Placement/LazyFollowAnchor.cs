using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Keeps this GameObject floating in front of the player with the same comfort-cone "lazy follow"
    /// the notification HUD uses (see <see cref="LazyFollowYaw"/>): the panel holds its place in the
    /// world while the head turns within the cone, then eases after it once it would leave view.
    ///
    /// Drives world position and rotation directly every LateUpdate, so it OVERRIDES any parent's
    /// position and rotation — but NOT the parent's scale. Parent it to something with a uniform
    /// scale of 1 (the XR Origin root, or nothing at all); do not leave it under a controller.
    ///
    /// Single responsibility: placement only. It knows nothing about what it is carrying, so it works
    /// for the inventory panel, a HUD, or any other world-space UI.
    /// </summary>
    public class LazyFollowAnchor : MonoBehaviour
    {
        [Header("Placement")]
        [Tooltip("Distance (m) the panel floats in front of the camera. Around 0.7–0.9 reads comfortably " +
                 "for a panel you interact with; the notification HUD sits further out at 1.6.")]
        [SerializeField, Min(0.2f)] private float _distance = 0.8f;

        [Tooltip("Offset (m) RIGHT of view-centre. 0 centres the panel; positive moves it right.")]
        [SerializeField] private float _rightOffset = 0f;

        [Tooltip("Offset (m) UP from view-centre. Slightly negative sits it just below eye level, " +
                 "which is the comfortable place to read and point at something.")]
        [SerializeField] private float _upOffset = -0.12f;

        [Header("Lazy follow — world-anchored, re-centres past the cone")]
        [Tooltip("Half-angle (deg) of the comfort cone: how far the player can rotate their head before " +
                 "the panel starts following. Within it the panel stays put in world space and slides " +
                 "across the view; past it, it eases back to centre.")]
        [SerializeField, Range(5f, 80f)] private float _followDeadzoneDeg = 30f;

        [Tooltip("Smoothing time (s) for the re-centre ease. Lower = snappier follow, higher = lazier.")]
        [SerializeField, Min(0.01f)] private float _followSmoothTime = 0.18f;

        [Header("Pitch")]
        [Tooltip("Tilt the panel to face the camera vertically as well as horizontally. Off keeps it " +
                 "upright, which is steadier but reads badly if the panel sits well above or below eye level.")]
        [SerializeField] private bool _faceCameraPitch = true;

        private readonly LazyFollowYaw _follow = new LazyFollowYaw();
        private Camera _camera;

        /// <summary>
        /// Snap to the current view instead of easing in — call when the panel is shown after being
        /// hidden, so it arrives in front of the player.
        /// </summary>
        public void Reanchor() => _follow.Reanchor();

        private void OnEnable()
        {
            _camera = Camera.main;
            _follow.Reanchor();

            // Re-place the panel AFTER the XR pose drivers have written this frame's head pose.
            // TrackedPoseDriver registers with [BeforeRenderOrder(-30000)], so an unordered callback
            // like this one runs after it. Without this the panel is positioned against last frame's
            // camera pose and visibly swims during head movement.
            Application.onBeforeRender += ApplyPlacement;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= ApplyPlacement;
        }

        private void LateUpdate()
        {
            if (!EnsureCamera()) return;

            // Yaw offset from the anchor to the panel's visual centre. A UIDocument world-space panel is
            // centred on its transform, so the placement offset IS the centre offset — no half-width
            // correction is needed here (unlike the notification HUD, whose canvas pivots top-right).
            float panelYawOffset = Mathf.Atan2(_rightOffset, _distance) * Mathf.Rad2Deg;

            // Advance the smoothing exactly ONCE per frame. ApplyPlacement deliberately does not solve,
            // or the ease would run at double rate on frames where both callbacks fire.
            _follow.Solve(_camera.transform.eulerAngles.y, panelYawOffset,
                          _followDeadzoneDeg, _followSmoothTime, Time.unscaledDeltaTime);

            ApplyPlacement();
        }

        // Places the panel from the ALREADY-SOLVED yaw and the camera's current pose. Safe to call
        // more than once a frame.
        private void ApplyPlacement()
        {
            if (!EnsureCamera()) return;

            Transform cam = _camera.transform;

            // Yaw-only anchor horizontally, plus a stable world-up offset vertically, so pitching the
            // head does not bob the panel.
            Quaternion yaw = Quaternion.Euler(0f, _follow.Yaw, 0f);
            Vector3 position = cam.position
                             + yaw * Vector3.forward * _distance
                             + yaw * Vector3.right * _rightOffset
                             + Vector3.up * _upOffset;

            transform.position = position;
            transform.rotation = _faceCameraPitch
                ? Quaternion.LookRotation(position - cam.position, Vector3.up)
                : yaw;
        }

        private bool EnsureCamera()
        {
            if (_camera != null) return true;

            _camera = Camera.main;
            if (_camera == null) return false;

            _follow.Reanchor();
            return true;
        }
    }
}
