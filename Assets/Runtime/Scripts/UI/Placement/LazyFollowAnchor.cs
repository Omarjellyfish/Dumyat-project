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
        [SerializeField] private float _upOffset = 0f;

        [Header("Lazy follow — world-anchored, re-centres past the cone")]
        [Tooltip("Half-angle (deg) of the comfort cone: how far the player can rotate their head before " +
                 "the panel starts following. Within it the panel stays put in world space and slides " +
                 "across the view; past it, it eases back to centre.")]
        [SerializeField, Range(5f, 80f)] private float _followDeadzoneDeg = 30f;

        [Tooltip("Smoothing time (s) for the re-centre ease. Lower = snappier follow, higher = lazier.")]
        [SerializeField, Min(0.01f)] private float _followSmoothTime = 0.18f;

        [Header("Teleports")]
        [Tooltip("If the camera jumps further than this in a single frame, treat it as a teleport and " +
                 "snap the panel back in front of the player instead of leaving it behind at its old " +
                 "world anchor. Set to 0 to disable.")]
        [SerializeField, Min(0f)] private float _teleportSnapDistance = 1f;

        [Header("Pitch")]
        [Tooltip("Tilt the panel to face the camera vertically as well as horizontally. Off keeps it " +
                 "upright, which is steadier but reads badly if the panel sits well above or below eye level.")]
        [SerializeField] private bool _faceCameraPitch = true;

        private readonly LazyFollowYaw _follow = new LazyFollowYaw();
        private Camera _camera;

        /// <summary>Camera position last frame, for spotting a teleport. Null until the first frame.</summary>
        private Vector3? _lastCameraPosition;

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

            SnapIfTeleported();

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

        /// <summary>
        /// Re-anchors the panel when the player has been moved rather than walked.
        ///
        /// This solver anchors to a WORLD yaw and holds it, which is what stops the panel swimming
        /// while the head turns — but it also means a panel is left behind wherever the player used to
        /// be when they are teleported somewhere else. Two cases hit this regularly:
        ///
        ///  * Teleport locomotion. The panel stays at the old spot, out of view, until the player
        ///    happens to turn far enough to drag it back.
        ///  * Arriving in a new scene by bus. <see cref="FadeTransitionHandler"/> places the player at
        ///    the ArrivalPoint on SceneLoadCompleteEvent, which is AFTER the new scene's objects have
        ///    run Start — so a panel that showed itself on Start (the consent prompt) anchored against
        ///    wherever the carried rig happened to be standing, not the spawn point.
        ///
        /// A per-frame distance test catches both without either of them having to know this component
        /// exists. The threshold is far above any real locomotion speed — a metre in one frame is
        /// roughly 90 m/s — so ordinary walking never trips it.
        /// </summary>
        private void SnapIfTeleported()
        {
            Vector3 position = _camera.transform.position;

            if (_teleportSnapDistance > 0f && _lastCameraPosition.HasValue &&
                Vector3.Distance(_lastCameraPosition.Value, position) > _teleportSnapDistance)
            {
                _follow.Reanchor();
            }

            _lastCameraPosition = position;
        }

        private bool EnsureCamera()
        {
            if (_camera != null) return true;

            _camera = Camera.main;
            if (_camera == null) return false;

            // A fresh camera means a fresh baseline: the distance from the old scene's camera to this
            // one is meaningless, and comparing against it would fire a redundant snap.
            _lastCameraPosition = null;
            _follow.Reanchor();
            return true;
        }
    }
}
