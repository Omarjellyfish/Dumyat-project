using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace DumyatRuntime
{
    /// <summary>
    /// Presents a <see cref="MediaSlideSO"/>: footage, stills, or — while the footage does not exist yet
    /// — a capture card stating what to shoot.
    ///
    /// One <see cref="VideoPlayer"/> and one RenderTexture are reused for every video slide. Fifteen
    /// players would each hold a decoder open; one that gets re-pointed costs a clip swap instead.
    ///
    /// The scrim and lockup are the SAME elements the scene slides use, because a caption over footage
    /// and a caption over a live stage are the same design problem. Video lifts the lockup clear of the
    /// progress bar via a class rather than a second lockup.
    /// </summary>
    public sealed class MediaSlideRenderer : MonoBehaviour, ISlideRenderer
    {
        [SerializeField] private PresentationView _view;

        [SerializeField, Tooltip("Resolution of the shared video render texture. 1920x1080 unless the " +
                                 "footage is authored larger.")]
        private Vector2Int _videoResolution = new Vector2Int(1920, 1080);

        private VisualElement _host;
        private VideoPlayer _player;
        private RenderTexture _videoTarget;
        private MediaSlideSO _current;
        private Label _timeLabel;
        private VisualElement _fill;
        private VisualElement _stateIcon;
        private Label _stateLabel;

        /// <summary>True while a clip is loaded and not paused. Read by the input relay for the P key.</summary>
        public bool IsPlaying => _player != null && _player.isPlaying;

        private void Awake()
        {
            if (_view == null) _view = GetComponentInParent<PresentationView>();
        }

        /// <summary>
        /// Builds the host container on first use, NOT in Awake.
        ///
        /// Awake order between components on the same GameObject is not something to rely on: building
        /// this in Awake meant it silently did nothing whenever PresentationView had not resolved its
        /// tree yet, and the failure looked like a slide that renders no content at all. Creating it on
        /// demand removes the ordering dependency entirely.
        /// </summary>
        private bool EnsureHost()
        {
            if (_host != null) return true;
            if (_view == null || !_view.IsReady) return false;

            _host = new VisualElement { name = "media-host" };
            _host.AddToClassList("media-host");
            _host.pickingMode = PickingMode.Ignore;
            _view.Content.Add(_host);
            _host.AddToClassList("hidden");
            return true;
        }

        public bool CanRender(SlideSO slide) => slide is MediaSlideSO;

        public void Hide()
        {
            if (_host != null) _host.AddToClassList("hidden");
            if (_view != null && _view.IsReady)
            {
                _view.Scrim.AddToClassList("hidden");
                _view.Lockup.AddToClassList("hidden");
                _view.Lockup.RemoveFromClassList("lockup--lifted");
            }
            // Stopped rather than paused: a clip left running behind a text slide keeps decoding.
            if (_player != null) _player.Stop();
            _current = null;
        }

        public void Show(SlideSO slide, PresentationDeckSO deck)
        {
            if (!EnsureHost()) return;
            if (!(slide is MediaSlideSO media))
            {
                Debug.LogWarning($"[MediaSlideRenderer] asked to render {slide?.GetType().Name}.", this);
                return;
            }

            _current = media;
            _host.RemoveFromClassList("hidden");
            _host.Clear();
            _timeLabel = null; _fill = null; _stateIcon = null; _stateLabel = null;

            PresentationThemeSO theme = deck != null ? deck.Theme : null;

            if (media.IsPending) { DrawCaptureCard(media, deck, theme); return; }

            switch (media.Layout)
            {
                case MediaSlideLayout.FullBleed: DrawFullBleed(media, deck, theme); break;
                case MediaSlideLayout.SideBySide: DrawSideBySide(media, theme); break;
                default: DrawInset(media, theme); break;
            }
        }

        // -------------------------------------------------------------------------
        // Capture card — the state the deck is in today
        // -------------------------------------------------------------------------

        private void DrawCaptureCard(MediaSlideSO media, PresentationDeckSO deck, PresentationThemeSO theme)
        {
            _view.Scrim.AddToClassList("hidden");
            _view.Lockup.AddToClassList("hidden");

            // The capture card draws its OWN eyebrow inside this host. The shared one lives in the
            // text-slide tree, which is hidden whenever a non-text renderer is showing — writing to it
            // from here produced a label nobody could see.
            string sectionName = deck != null ? deck.ResolveSectionName(media) : media.SectionName;
            if (!string.IsNullOrEmpty(sectionName))
            {
                var eyebrow = new Label($"{sectionName.ToUpperInvariant()} · SECTION {media.SectionNumber:00}");
                eyebrow.AddToClassList("media-eyebrow");
                if (theme != null) eyebrow.style.color = theme.Accent;
                _host.Add(eyebrow);
            }

            var card = new VisualElement();
            card.AddToClassList("capture-card");
            _host.Add(card);

            var title = new Label(media.Title ?? string.Empty);
            title.AddToClassList("media-title");
            if (theme != null) title.style.color = theme.Ink;
            card.Add(title);

            if (!string.IsNullOrEmpty(media.Caption))
            {
                var cap = new Label(media.Caption);
                cap.AddToClassList("media-kicker");
                if (theme != null) cap.style.color = theme.InkMuted;
                card.Add(cap);
            }

            var rule = new VisualElement();
            rule.AddToClassList("capture-rule");
            card.Add(rule);

            var badge = new Label("FOOTAGE PENDING");
            badge.AddToClassList("capture-badge");
            card.Add(badge);

            if (!string.IsNullOrEmpty(media.CaptureBrief))
            {
                var brief = new Label(media.CaptureBrief);
                brief.AddToClassList("capture-brief");
                if (theme != null) brief.style.color = theme.Ink;
                card.Add(brief);
            }

            if (!string.IsNullOrEmpty(media.CaptureLength))
            {
                var len = new Label("TARGET LENGTH   " + media.CaptureLength);
                len.AddToClassList("capture-length");
                if (theme != null) len.style.color = theme.InkMuted;
                card.Add(len);
            }
        }

        // -------------------------------------------------------------------------
        // Layouts
        // -------------------------------------------------------------------------

        private void DrawFullBleed(MediaSlideSO media, PresentationDeckSO deck, PresentationThemeSO theme)
        {
            var frame = new VisualElement();
            frame.AddToClassList("media-fullbleed");
            _host.Add(frame);
            AssignMedia(frame, media);

            // Full bleed means full bleed: no eyebrow, the image gets the whole frame.
            _view.Scrim.RemoveFromClassList("hidden");
            DrawLockup(media, theme, lifted: media.Clip != null);

            if (media.Clip != null) DrawVideoBar(theme);
        }

        private void DrawSideBySide(MediaSlideSO media, PresentationThemeSO theme)
        {
            _view.Scrim.AddToClassList("hidden");
            _view.Lockup.AddToClassList("hidden");

            var row = new VisualElement();
            row.AddToClassList("media-split");
            _host.Add(row);

            var text = new VisualElement();
            text.AddToClassList("media-split-text");
            var title = new Label(media.Title ?? string.Empty);
            title.AddToClassList("media-title");
            if (theme != null) title.style.color = theme.Ink;
            text.Add(title);

            if (!string.IsNullOrEmpty(media.Caption))
            {
                var cap = new Label(media.Caption);
                cap.AddToClassList("media-body");
                if (theme != null) cap.style.color = theme.InkMuted;
                text.Add(cap);
            }
            row.Add(text);

            var pic = new VisualElement();
            pic.AddToClassList("media-split-pic");
            AssignMedia(pic, media);
            row.Add(pic);
        }

        private void DrawInset(MediaSlideSO media, PresentationThemeSO theme)
        {
            _view.Scrim.AddToClassList("hidden");
            _view.Lockup.AddToClassList("hidden");

            var title = new Label(media.Title ?? string.Empty);
            title.AddToClassList("media-title");
            title.AddToClassList("media-title--inset");
            if (theme != null) title.style.color = theme.Ink;
            _host.Add(title);

            if (!string.IsNullOrEmpty(media.Caption))
            {
                var cap = new Label(media.Caption);
                cap.AddToClassList("media-kicker");
                if (theme != null) cap.style.color = theme.InkMuted;
                _host.Add(cap);
            }

            var row = new VisualElement();
            row.AddToClassList("media-inset-row");
            _host.Add(row);

            int shown = 0;
            foreach (Sprite s in media.Stills)
            {
                if (s == null || shown >= 4) continue;
                var cell = new VisualElement();
                cell.AddToClassList("media-inset-cell");
                cell.style.backgroundImage = new StyleBackground(s);
                cell.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
                row.Add(cell);
                shown++;
            }
        }

        // -------------------------------------------------------------------------
        // Shared pieces
        // -------------------------------------------------------------------------

        private void AssignMedia(VisualElement target, MediaSlideSO media)
        {
            if (media.Clip != null)
            {
                RenderTexture rt = EnsurePlayer(media);
                target.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
                target.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
                return;
            }

            if (media.Stills != null && media.Stills.Length > 0 && media.Stills[0] != null)
            {
                target.style.backgroundImage = new StyleBackground(media.Stills[0]);
                target.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
            }
        }

        private RenderTexture EnsurePlayer(MediaSlideSO media)
        {
            if (_videoTarget == null)
            {
                _videoTarget = new RenderTexture(
                    Mathf.Clamp(_videoResolution.x, 256, 4096),
                    Mathf.Clamp(_videoResolution.y, 144, 4096), 0, RenderTextureFormat.ARGB32)
                { name = "RT_PresentationVideo" };
                _videoTarget.Create();
            }

            if (_player == null)
            {
                _player = gameObject.AddComponent<VideoPlayer>();
                _player.playOnAwake = false;
                _player.renderMode = VideoRenderMode.RenderTexture;
                _player.targetTexture = _videoTarget;
                _player.skipOnDrop = true;
            }

            _player.isLooping = media.Loop;
            _player.audioOutputMode = media.PlayAudio ? VideoAudioOutputMode.Direct : VideoAudioOutputMode.None;

            if (_player.clip != media.Clip)
            {
                _player.Stop();
                _player.clip = media.Clip;
            }

            if (media.AutoPlay) _player.Play();
            return _videoTarget;
        }

        private void DrawLockup(MediaSlideSO media, PresentationThemeSO theme, bool lifted)
        {
            _view.Lockup.RemoveFromClassList("hidden");
            _view.Lockup.EnableInClassList("lockup--lifted", lifted);

            _view.LockupPlace.text = media.Title ?? string.Empty;
            _view.LockupArabic.AddToClassList("hidden");

            bool hasCaption = !string.IsNullOrEmpty(media.Caption);
            _view.LockupCaption.EnableInClassList("hidden", !hasCaption);
            if (hasCaption) _view.LockupCaption.text = media.Caption;

            if (theme != null)
            {
                _view.LockupRule.style.backgroundColor = theme.Accent;
                _view.LockupPlace.style.color = theme.Ink;
                _view.LockupCaption.style.color = theme.InkMuted;
            }
        }

        private void DrawVideoBar(PresentationThemeSO theme)
        {
            var bar = new VisualElement();
            bar.AddToClassList("video-bar");
            _host.Add(bar);

            var track = new VisualElement();
            track.AddToClassList("video-track");
            _fill = new VisualElement();
            _fill.AddToClassList("video-fill");
            if (theme != null) _fill.style.backgroundColor = theme.Accent;
            track.Add(_fill);
            bar.Add(track);

            var meta = new VisualElement();
            meta.AddToClassList("video-meta");

            var state = new VisualElement();
            state.AddToClassList("video-state");
            _stateIcon = new VisualElement();
            _stateIcon.AddToClassList("video-state-icon");
            _stateLabel = new Label("PLAYING");
            _stateLabel.AddToClassList("video-state-label");
            state.Add(_stateIcon);
            state.Add(_stateLabel);

            _timeLabel = new Label("0:00");
            _timeLabel.AddToClassList("video-time");
            if (theme != null) _timeLabel.style.color = theme.InkMuted;

            meta.Add(state);
            meta.Add(_timeLabel);
            bar.Add(meta);
        }

        /// <summary>Pause or resume the current clip. Bound to P by the input relay.</summary>
        public void TogglePause()
        {
            if (_player == null || _player.clip == null) return;
            if (_player.isPlaying) _player.Pause(); else _player.Play();
        }

        private void Update()
        {
            if (_player == null || _player.clip == null || _fill == null) return;

            double length = _player.length;
            double time = _player.time;
            float t = length > 0.01 ? Mathf.Clamp01((float)(time / length)) : 0f;
            _fill.style.width = new Length(t * 100f, LengthUnit.Percent);

            if (_timeLabel != null)
                _timeLabel.text = Format(time) + " / " + Format(length);

            bool playing = _player.isPlaying;
            if (_stateLabel != null)
                _stateLabel.text = playing
                    ? (_current != null && _current.Loop ? "PLAYING · LOOP" : "PLAYING")
                    : "PAUSED";
            // Coral when paused: a stalled clip mid-talk is a problem to notice.
            if (_stateIcon != null)
                _stateIcon.EnableInClassList("video-state-icon--paused", !playing);
        }

        private static string Format(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds)) return "0:00";
            int total = (int)seconds;
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        private void OnDestroy()
        {
            if (_videoTarget != null) { _videoTarget.Release(); Destroy(_videoTarget); }
        }
    }
}
