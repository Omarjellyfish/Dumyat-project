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
        [Tooltip("Card used for NotificationStyle.Toast (quest progress, frequent beats). " +
                 "Auto-found in children if empty.")]
        [SerializeField] private NotificationToastView _toastView;

        [Tooltip("Card used for NotificationStyle.Medal (quest/sale completion). " +
                 "Auto-found in children if empty.")]
        [SerializeField] private NotificationMedalView _medalView;

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

        // A List, not a Queue: coalescing has to REPLACE an entry where it stands (so a superseded
        // card keeps its place in line rather than jumping to the back). Order is still strict FIFO —
        // taken from index 0, appended at the end.
        private readonly List<NotificationData> _queue = new List<NotificationData>();
        private Coroutine _runner;

        /// <summary>
        /// The view drawing the card on screen right now. Held because the manager animates and
        /// positions whichever card is up, and that is no longer a fixed choice — a quest ticking
        /// forward draws as a toast while its completion draws as a medal.
        /// </summary>
        private NotificationView _active;

        // What the card on screen is showing, and whether it can still be superseded in place. Goes
        // false for the fade-out: past that point the card is leaving, so a late update is a fresh
        // card rather than a rewrite of one the player is no longer reading.
        private NotificationData _activeData;
        private bool _activeReplaceable;

        // Written by the playback loop, re-applied when a card is rebuilt under it: Build() makes a
        // new CanvasGroup at full alpha, which would flash if it replaced a card mid-fade.
        private float _cardAlpha;

        // Hold-phase timer. A field rather than a local so coalescing can restart it — a card whose
        // text changes with one second left would show the new number for one second.
        private float _holdElapsed;

        // Shared comfort-cone follow, identical to the one driving the inventory panel.
        private readonly LazyFollowYaw _follow = new LazyFollowYaw();

        private void Awake()
        {
            if (_toastView == null) _toastView = GetComponentInChildren<NotificationToastView>(true);
            if (_medalView == null) _medalView = GetComponentInChildren<NotificationMedalView>(true);

            _camera = Camera.main;
            BuildCanvas();

            if (_toastView == null && _medalView == null)
            {
                Debug.LogWarning($"{nameof(NotificationHudManager)} on '{name}' has no NotificationView " +
                                 "assigned/in children — notifications will not render.", this);
                return;
            }

            if (_toastView != null) _toastView.Attach(_root);
            if (_medalView != null) _medalView.Attach(_root);
        }

        /// <summary>
        /// The view for <paramref name="style"/>, falling back to the other one when only a single
        /// view is present. The fallback keeps a scene that was authored before styles existed — and
        /// so carries just one view — showing its notifications rather than silently dropping them.
        /// </summary>
        private NotificationView ViewFor(NotificationStyle style)
        {
            NotificationView preferred = style == NotificationStyle.Toast ? _toastView : (NotificationView)_medalView;
            if (preferred != null) return preferred;

            return style == NotificationStyle.Toast ? _medalView : (NotificationView)_toastView;
        }

        private void BuildCanvas()
        {
            var go = new GameObject("Notification Canvas", typeof(RectTransform), typeof(Canvas));
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            _root = (RectTransform)go.transform;
            _root.SetParent(transform, false);
            _root.pivot = new Vector2(1f, 1f);          // top-right corner is the anchor point
            _root.sizeDelta = new Vector2(600f, 400f);  // >= the largest card; spare space extends down-left
            _root.localScale = Vector3.one * _canvasScale;
        }

        // --- INotificationService ---------------------------------------------------

        public void Enqueue(NotificationData data)
        {
            if (TryReplace(data)) return;

            _queue.Add(data);
            if (_runner == null) _runner = StartCoroutine(Run());
        }

        public void Clear() => _queue.Clear();

        /// <summary>
        /// Supersedes an earlier card carrying the same <see cref="NotificationData.CoalesceKey"/> —
        /// the one on screen if it is still holding, otherwise the queued one — and reports whether it
        /// did. See <see cref="NotificationData.CoalesceKey"/> for why this exists.
        ///
        /// Styles must match: a progress toast and a completion medal are different cards drawn by
        /// different views, so one cannot be rewritten into the other. Mismatched styles fall through
        /// and queue normally, which is the correct reading anyway — those are two distinct beats.
        /// </summary>
        private bool TryReplace(NotificationData data)
        {
            if (string.IsNullOrEmpty(data.CoalesceKey)) return false;

            if (_activeReplaceable && _active != null
                && _activeData.Style == data.Style
                && _activeData.CoalesceKey == data.CoalesceKey)
            {
                _activeData = data;
                _active.Build(data);

                // Rebuilt card starts fully opaque and with a full countdown bar; put it back where
                // the animation actually is, then give the new content its own full hold.
                CanvasGroup g = _active.GroupHandle;
                if (g != null) g.alpha = _cardAlpha;
                _holdElapsed = 0f;
                _active.SetProgress(1f);
                return true;
            }

            for (int i = 0; i < _queue.Count; i++)
            {
                if (_queue[i].Style != data.Style || _queue[i].CoalesceKey != data.CoalesceKey) continue;
                _queue[i] = data;
                return true;
            }

            return false;
        }

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
            float cardHalfWidth = (_active != null && _active.ContentHandle != null)
                ? _active.ContentHandle.rect.width * 0.5f * _canvasScale
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
                NotificationData data = _queue[0];
                _queue.RemoveAt(0);

                // Resolved per notification, not once at Awake: the style travels with the card.
                _active = ViewFor(data.Style);
                if (_active == null) { _runner = null; yield break; }

                _follow.Reanchor();      // snap the fresh card into the current view, then world-anchor
                _activeData = data;
                _activeReplaceable = true;
                _cardAlpha = 0f;
                _active.Build(data);
                if (_active.GroupHandle == null) { _activeReplaceable = false; yield return null; continue; }

                // Handles are re-read every frame rather than cached across the loop: a coalesced
                // update rebuilds the card underneath us, which destroys the old CanvasGroup/Card and
                // would leave a cached reference animating a dead object.

                // Fade + slide + scale in (unscaled, so a paused game still animates it).
                float t = 0f;
                while (t < FadeIn)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / FadeIn);
                    float e = 1f - (1f - k) * (1f - k);   // ease-out
                    _cardAlpha = k;
                    ApplyAlpha(k);
                    RectTransform c = _active.ContentHandle;
                    if (c != null)
                    {
                        c.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, e);
                        c.anchoredPosition = new Vector2(Mathf.Lerp(14f, 0f, e), 0f);
                    }
                    yield return null;
                }
                _cardAlpha = 1f;
                ApplyAlpha(1f);
                if (_active.ContentHandle != null)
                {
                    _active.ContentHandle.localScale = Vector3.one;
                    _active.ContentHandle.anchoredPosition = Vector2.zero;
                }

                // Hold + countdown bar. _holdElapsed is a field so a coalesced update can restart it.
                _holdElapsed = 0f;
                while (_holdElapsed < _holdSeconds)
                {
                    _holdElapsed += Time.unscaledDeltaTime;
                    _active.SetProgress(1f - _holdElapsed / _holdSeconds);
                    yield return null;
                }
                _active.SetProgress(0f);

                // Fade out. The card is leaving, so it stops accepting in-place updates here.
                _activeReplaceable = false;
                t = 0f;
                while (t < FadeOut)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / FadeOut);
                    _cardAlpha = 1f - k;
                    ApplyAlpha(_cardAlpha);
                    RectTransform c = _active.ContentHandle;
                    if (c != null) c.localScale = Vector3.one * Mathf.Lerp(1f, 0.98f, k);
                    yield return null;
                }
                ApplyAlpha(0f);
                _active.HideCard();
                _active = null;
            }

            _activeReplaceable = false;
            _runner = null;
        }

        private void ApplyAlpha(float alpha)
        {
            CanvasGroup g = _active != null ? _active.GroupHandle : null;
            if (g != null) g.alpha = alpha;
        }
    }
}
