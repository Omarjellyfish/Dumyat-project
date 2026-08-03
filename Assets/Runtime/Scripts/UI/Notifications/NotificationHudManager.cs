using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Generic notification HUD (implements <see cref="INotificationService"/>). Owns a world-space canvas that
    /// floats at the FAR TOP-RIGHT of the player's view, a FIFO queue, the 7s show timer, and the fade / slide /
    /// countdown animation. The visual style is delegated to the assigned <see cref="NotificationView"/> (Toast
    /// or Medal) — swap it to change the look. Passive HUD: nothing here is ray-clickable, so it never blocks
    /// the controller ray to the scale behind it.
    ///
    /// Register in a scene installer, e.g. <c>Register&lt;NotificationHudManager, INotificationService&gt;(...)</c>.
    /// </summary>
    public class NotificationHudManager : MonoBehaviour, INotificationService
    {
        [Header("Style")]
        [Tooltip("The notification visual (Toast card or Achievement medal). Auto-found in children if empty.")]
        [SerializeField] private NotificationView _view;

        [Header("Timing")]
        [Tooltip("Seconds each notification stays fully shown (before fading out).")]
        [SerializeField, Min(0.5f)] private float _holdSeconds = 7f;

        [Header("Placement — far top-right of view")]
        [Tooltip("Distance (m) the HUD floats in front of the camera.")]
        [SerializeField, Min(0.3f)] private float _distance = 1.6f;
        [Tooltip("How far RIGHT of view-centre the panel's top-right corner sits (m).")]
        [SerializeField] private float _rightOffset = 1.05f;
        [Tooltip("How far UP from view-centre the panel's top-right corner sits (m).")]
        [SerializeField] private float _upOffset = 0.72f;
        [Tooltip("World-space canvas scale: panel metres = canvas units × this.")]
        [SerializeField, Min(0.0001f)] private float _canvasScale = 0.0017f;

        [Header("Lazy follow — world-anchored, re-centres past the cone")]
        [Tooltip("Half-angle (deg) of the comfort cone: how far the player can rotate their head " +
                 "before the panel starts following. Within it, the panel stays put in world space " +
                 "and slides across the view; past it, the panel eases back to the top-right.")]
        [SerializeField, Range(5f, 80f)] private float _followDeadzoneDeg = 30f;
        [Tooltip("Smoothing time (s) for the re-centre ease when the head rotates past the cone. " +
                 "Lower = snappier follow, higher = lazier.")]
        [SerializeField, Min(0.01f)] private float _followSmoothTime = 0.18f;

        private const float FadeIn = 0.4f;
        private const float FadeOut = 0.35f;

        private RectTransform _root;      // the world canvas we reposition every frame
        private Camera _camera;
        private readonly Queue<NotificationData> _queue = new Queue<NotificationData>();
        private Coroutine _runner;

        // Shared comfort-cone follow, identical to the one driving the inventory panel.
        private readonly LazyFollowYaw _follow = new LazyFollowYaw();

        private void Awake()
        {
            if (_view == null) _view = GetComponentInChildren<NotificationView>(true);
            _camera = Camera.main;
            BuildCanvas();

            if (_view == null)
                Debug.LogWarning($"{nameof(NotificationHudManager)} on '{name}' has no NotificationView " +
                                 "assigned/in children — notifications will not render.", this);
            else
                _view.Attach(_root);
        }

        private void BuildCanvas()
        {
            var go = new GameObject("Notification Canvas", typeof(RectTransform), typeof(Canvas));
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            _root = (RectTransform)go.transform;
            _root.SetParent(transform, false);
            _root.pivot = new Vector2(1f, 1f);          // top-right corner is the anchor point
            _root.sizeDelta = new Vector2(480f, 340f);  // >= the largest card; spare space extends down-left
            _root.localScale = Vector3.one * _canvasScale;
        }

        // --- INotificationService ---------------------------------------------------

        public void Enqueue(NotificationData data)
        {
            _queue.Enqueue(data);
            if (_runner == null) _runner = StartCoroutine(Run());
        }

        public void Clear() => _queue.Clear();

        // --- Placement (world-anchored top-right, lazy-follow past the cone) ---------

        private void LateUpdate()
        {
            if (_root == null) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            Transform cam = _camera.transform;

            // Clamp the card's VISUAL CENTRE against the cone, not the canvas anchor. The canvas
            // pivot is the top-right corner (_rightOffset from the forward ray) and the card body
            // hangs down-left from it, so its centre is half a card-width further in. Clamping the
            // corner would let the body swing much further left than right; centring on the card
            // keeps looking left and right symmetric.
            float cardHalfWidth = (_view != null && _view.ContentHandle != null)
                ? _view.ContentHandle.rect.width * 0.5f * _canvasScale
                : 0f;
            float centreRightOffset = _rightOffset - cardHalfWidth;
            float panelYawOffset = Mathf.Atan2(centreRightOffset, _distance) * Mathf.Rad2Deg;

            float followYaw = _follow.Solve(cam.eulerAngles.y, panelYawOffset,
                                            _followDeadzoneDeg, _followSmoothTime,
                                            Time.unscaledDeltaTime);

            // Yaw-only anchor for the horizontal; a stable world-up offset for the vertical so
            // pitching the head doesn't bob the panel.
            Quaternion yaw = Quaternion.Euler(0f, followYaw, 0f);
            Vector3 fwd = yaw * Vector3.forward;
            Vector3 right = yaw * Vector3.right;
            _root.position = cam.position + fwd * _distance + Vector3.up * _upOffset + right * _rightOffset;
            _root.rotation = Quaternion.LookRotation(_root.position - cam.position, Vector3.up);
        }

        // --- Queue playback ---------------------------------------------------------

        private IEnumerator Run()
        {
            while (_queue.Count > 0)
            {
                NotificationData data = _queue.Dequeue();
                if (_view == null) { _runner = null; yield break; }

                _follow.Reanchor();      // snap the fresh card into the current view, then world-anchor
                _view.Build(data);
                CanvasGroup g = _view.GroupHandle;
                RectTransform c = _view.ContentHandle;
                if (g == null) { yield return null; continue; }

                // Fade + slide + scale in (unscaled, so a paused game still animates it).
                float t = 0f;
                while (t < FadeIn)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / FadeIn);
                    float e = 1f - (1f - k) * (1f - k);   // ease-out
                    g.alpha = k;
                    if (c != null)
                    {
                        c.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, e);
                        c.anchoredPosition = new Vector2(Mathf.Lerp(14f, 0f, e), 0f);
                    }
                    yield return null;
                }
                g.alpha = 1f;
                if (c != null) { c.localScale = Vector3.one; c.anchoredPosition = Vector2.zero; }

                // Hold + countdown bar.
                t = 0f;
                while (t < _holdSeconds)
                {
                    t += Time.unscaledDeltaTime;
                    _view.SetProgress(1f - t / _holdSeconds);
                    yield return null;
                }
                _view.SetProgress(0f);

                // Fade out.
                t = 0f;
                while (t < FadeOut)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / FadeOut);
                    g.alpha = 1f - k;
                    if (c != null) c.localScale = Vector3.one * Mathf.Lerp(1f, 0.98f, k);
                    yield return null;
                }
                g.alpha = 0f;
                _view.HideCard();
            }

            _runner = null;
        }
    }
}
