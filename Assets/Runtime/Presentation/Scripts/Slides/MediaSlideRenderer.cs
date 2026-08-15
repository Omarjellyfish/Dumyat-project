using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace DumyatRuntime
{
    /// <summary>
    /// Presents a <see cref="MediaSlideSO"/>: footage, stills, or — while the footage does not exist yet
    /// — a capture card stating what to shoot.
    ///
    /// Players and render textures come from <see cref="VideoPlayerPool"/>, which is where the decoder
    /// and resolution reasoning lives; this file is layout.
    ///
    /// The scrim and lockup are the SAME elements the scene slides use, because a caption over footage
    /// and a caption over a live stage are the same design problem. Video lifts the lockup clear of the
    /// progress bar via a class rather than a second lockup.
    ///
    /// EVERYTHING IS LAID OUT IN 1920x1080 REFERENCE PIXELS, the space the whole stylesheet is authored
    /// in — the panel scales it to the projector. Sizes computed here (grid tiles, crop windows) are in
    /// the same units as the px values in the USS, so the two cannot disagree.
    /// </summary>
    public sealed class MediaSlideRenderer : MonoBehaviour, ISlideRenderer
    {
        private const float RefWidth = 1920f;
        private const float RefHeight = 1080f;

        /// <summary>Margins of the area the grid may use, clear of the title above and the rail below.</summary>
        private const float GridSideMargin = 120f;
        private const float GridTopWithTitle = 330f;
        private const float GridTopBare = 140f;
        private const float GridBottomMargin = 160f;
        private const float GridGap = 24f;

        [SerializeField] private PresentationView _view;

        [SerializeField, Tooltip("Output resolution the deck is being presented at. Left empty, the " +
                                 "fallback width below is used.")]
        private PresentationQualitySO _quality;

        [SerializeField, Tooltip("Largest render texture width any single clip may be given, whatever " +
                                 "its source resolution. Guards a 4K clip from allocating 4K of buffer " +
                                 "to be drawn in a quarter-width tile.\n\nOnly used when no quality " +
                                 "asset is assigned above.")]
        private int _maxVideoWidth = 1920;

        // Video render textures are sized from the CLIP, not from how wide they are drawn — see
        // VideoPlayerPool.TargetSize. A per-tile size meant reallocating on every navigation, which
        // freed textures the renderer was still drawing and crashed the Editor.

        private VisualElement _host;
        private VideoPlayerPool _pool;
        private MediaSlideSO _current;

        private Label _timeLabel;
        private VisualElement _fill;
        private VisualElement _stateIcon;
        private Label _stateLabel;
        private VisualElement _theatrePause;

        /// <summary>When the chrome should start fading. Infinity means it never does.</summary>
        private float _briefEndsAt = float.PositiveInfinity;
        private bool _briefFaded;

        /// <summary>True while a clip is loaded and not paused. Read by the input relay for the P key.</summary>
        public bool IsPlaying => _pool != null && _pool.AnyPlaying;

        private void Awake()
        {
            if (_view == null) _view = GetComponentInParent<PresentationView>();

            // The cap tracks the output resolution: a clip drawn full-bleed on a 4K screen has four
            // times the pixels to fill, and capping it at 1920 would upscale the one thing on the slide
            // the audience is actually looking at. It still never exceeds the clip's own resolution.
            int maxWidth = _quality != null ? _quality.MaxVideoWidth : _maxVideoWidth;
            _pool = new VideoPlayerPool(transform, maxWidth);
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
                ShowChrome();
            }

            _pool?.StopAll();
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
            _timeLabel = null; _fill = null; _stateIcon = null; _stateLabel = null; _theatrePause = null;

            // Chrome is restored first so a layout that hides it is the only thing that can, and
            // arriving from theatre mode never leaves the footer missing. This also clears any fade the
            // previous slide's brief left on the shared elements.
            ShowChrome();
            _briefEndsAt = float.PositiveInfinity;
            _briefFaded = false;

            _pool.BeginSlide();

            PresentationThemeSO theme = deck != null ? deck.Theme : null;

            if (media.IsPending) DrawCaptureCard(media, deck, theme);
            else
            {
                switch (media.Layout)
                {
                    case MediaSlideLayout.FullBleed:  DrawFullBleed(media, theme); break;
                    case MediaSlideLayout.SideBySide: DrawSideBySide(media, theme); break;
                    case MediaSlideLayout.Inset:      DrawInset(media, theme); break;
                    case MediaSlideLayout.Grid:       DrawGrid(media, theme); break;
                    case MediaSlideLayout.Theatre:    DrawTheatre(media); break;
                    default:                          DrawWindow(media, theme); break;
                }
            }

            _pool.EndSlide();
        }

        // -------------------------------------------------------------------------
        // Capture card — the state most of this deck is in today
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
        // Single-item layouts
        // -------------------------------------------------------------------------

        private void DrawFullBleed(MediaSlideSO media, PresentationThemeSO theme)
        {
            var frame = new VisualElement();
            frame.AddToClassList("media-fullbleed");
            _host.Add(frame);

            MediaItem item = media.Primary;
            Paint(frame, item, media, RefWidth, ScaleMode.ScaleAndCrop);

            // Full bleed means full bleed: no eyebrow, the image gets the whole frame.
            _view.Scrim.RemoveFromClassList("hidden");
            DrawLockup(media, theme, lifted: item.Clip != null);

            if (item.Clip != null) DrawVideoBar(theme);

            // The brief: label the slide, then get out of the way. Only for footage — a still has
            // nothing to reveal by fading its own caption away.
            _briefEndsAt = item.Clip != null && media.BriefSeconds > 0f
                ? Time.unscaledTime + media.BriefSeconds
                : float.PositiveInfinity;
            _briefFaded = false;
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

            // A still keeps its whole frame: the split panel is an arbitrary shape, and cropping a
            // drawing whose edges carry content to fill it is exactly the wrong trade. Footage still
            // crops, because a clip in this panel is 16:9 against a panel that is nearly 16:9.
            //
            // The modifier is what stops a fitted still landing flush against the screen edge — the
            // bare panel deliberately runs to it, which only reads correctly for cropped footage.
            MediaItem item = media.Primary;
            bool isStill = item.Clip == null;
            if (isStill) pic.AddToClassList("media-split-pic--still");

            Paint(pic, item, media, RefWidth * 0.5f,
                  isStill ? ScaleMode.ScaleToFit : ScaleMode.ScaleAndCrop);
            row.Add(pic);
        }

        private void DrawInset(MediaSlideSO media, PresentationThemeSO theme)
        {
            _view.Scrim.AddToClassList("hidden");
            _view.Lockup.AddToClassList("hidden");

            DrawHeading(media, theme);

            var row = new VisualElement();
            row.AddToClassList("media-inset-row");
            _host.Add(row);

            int shown = 0;
            foreach (MediaItem item in media.Items)
            {
                if (!item.HasContent || shown >= 4) continue;
                var cell = new VisualElement();
                cell.AddToClassList("media-inset-cell");
                Paint(cell, item, media, RefWidth / 4f, ScaleMode.ScaleAndCrop);
                row.Add(cell);
                shown++;
            }
        }

        // -------------------------------------------------------------------------
        // Grid — tiles sized to their own aspect, so nothing is cropped or letterboxed
        // -------------------------------------------------------------------------

        private void DrawGrid(MediaSlideSO media, PresentationThemeSO theme)
        {
            _view.Scrim.AddToClassList("hidden");
            _view.Lockup.AddToClassList("hidden");

            bool hasTitle = !string.IsNullOrEmpty(media.Title);
            if (hasTitle) DrawHeading(media, theme);

            var items = new List<MediaItem>();
            foreach (MediaItem item in media.Items)
                if (item.HasContent) items.Add(item);
            if (items.Count == 0) return;

            int columns = media.GridColumns > 0 ? media.GridColumns : AutoColumns(items.Count);
            columns = Mathf.Clamp(columns, 1, items.Count);
            int rows = Mathf.CeilToInt(items.Count / (float)columns);

            float areaWidth = RefWidth - GridSideMargin * 2f;
            float areaTop = hasTitle ? GridTopWithTitle : GridTopBare;
            float areaHeight = RefHeight - GridBottomMargin - areaTop;
            float slotHeight = (areaHeight - GridGap * (rows - 1)) / rows;

            var stack = new VisualElement();
            stack.AddToClassList("media-grid");
            stack.style.top = areaTop;
            stack.style.left = GridSideMargin;
            stack.style.width = areaWidth;
            stack.style.height = areaHeight;
            _host.Add(stack);

            for (int r = 0; r < rows; r++)
            {
                int first = r * columns;
                int count = Mathf.Min(columns, items.Count - first);

                // Lay the row out at the slot height, then shrink it if the tiles' own aspects make it
                // wider than the area. Shrinking the HEIGHT is what keeps every tile's aspect exact —
                // clamping widths instead is precisely the squash this layout exists to avoid.
                float aspectSum = 0f;
                for (int i = 0; i < count; i++) aspectSum += items[first + i].Aspect;

                float tileHeight = slotHeight;
                float rowWidth = tileHeight * aspectSum + GridGap * (count - 1);
                if (rowWidth > areaWidth && aspectSum > 0.01f)
                    tileHeight = (areaWidth - GridGap * (count - 1)) / aspectSum;

                var row = new VisualElement();
                row.AddToClassList("media-grid-row");
                row.style.height = slotHeight;
                if (r < rows - 1) row.style.marginBottom = GridGap;
                stack.Add(row);

                for (int i = 0; i < count; i++)
                {
                    MediaItem item = items[first + i];
                    float tileWidth = tileHeight * item.Aspect;

                    var tile = new VisualElement();
                    tile.AddToClassList("media-tile");
                    tile.style.width = tileWidth;
                    tile.style.height = tileHeight;
                    if (i < count - 1) tile.style.marginRight = GridGap;

                    // The tile is exactly the source aspect, so stretching is lossless — and unlike
                    // scale-to-fit it leaves no bars of its own inside the tile.
                    Paint(tile, item, media, tileWidth, ScaleMode.StretchToFill);

                    if (!string.IsNullOrEmpty(item.Caption))
                    {
                        var cap = new Label(item.Caption.ToUpperInvariant());
                        cap.AddToClassList("media-tile-caption");
                        tile.Add(cap);
                    }

                    row.Add(tile);
                }
            }
        }

        /// <summary>2 across, 3 across, 4 as a 2x2, 5 and 6 as two rows of three.</summary>
        private static int AutoColumns(int count)
        {
            if (count <= 3) return count;
            if (count == 4) return 2;
            return 3;
        }

        // -------------------------------------------------------------------------
        // Theatre — one clip, the whole screen, nothing over it
        // -------------------------------------------------------------------------

        private void DrawTheatre(MediaSlideSO media)
        {
            _view.Scrim.AddToClassList("hidden");
            _view.Lockup.AddToClassList("hidden");
            HideChrome();

            var frame = new VisualElement();
            // Opaque in USS rather than tinted from the theme: a theatre slide wants no backdrop, so
            // the ground behind this element has been cleared to transparent. Under a FITTED clip the
            // letterbox bars are whatever this element paints, and "nothing" is not a colour.
            frame.AddToClassList("media-theatre");
            _host.Add(frame);

            ScaleMode fit = media.TheatreFit == TheatreFit.Fill
                ? ScaleMode.ScaleAndCrop
                : ScaleMode.ScaleToFit;

            Paint(frame, media.Primary, media, RefWidth, fit);

            // Nothing while it plays; a coral marker fades in only when paused, because a stalled clip
            // mid-talk is the one thing worth interrupting an uninterrupted frame for.
            _theatrePause = new VisualElement();
            _theatrePause.AddToClassList("theatre-pause");
            _theatrePause.AddToClassList("hidden");
            _host.Add(_theatrePause);
        }

        // -------------------------------------------------------------------------
        // Window — a deliberate crop, anchored, with the copy taking the rest
        // -------------------------------------------------------------------------

        private void DrawWindow(MediaSlideSO media, PresentationThemeSO theme)
        {
            _view.Scrim.AddToClassList("hidden");
            _view.Lockup.AddToClassList("hidden");

            MediaWindow w = media.Window;
            float windowWidth = Mathf.Clamp01(w.Size.x <= 0f ? 0.36f : w.Size.x) * RefWidth;
            float windowHeight = Mathf.Clamp01(w.Size.y <= 0f ? 0.82f : w.Size.y) * RefHeight;

            var frame = new VisualElement();
            frame.AddToClassList("media-window");
            frame.style.width = windowWidth;
            frame.style.height = windowHeight;
            frame.style.borderTopLeftRadius = w.Corner;
            frame.style.borderTopRightRadius = w.Corner;
            frame.style.borderBottomLeftRadius = w.Corner;
            frame.style.borderBottomRightRadius = w.Corner;
            AnchorWindow(frame, w.Anchor, windowWidth, windowHeight);
            _host.Add(frame);

            MediaItem item = media.Primary;
            float aspect = item.Aspect;
            float zoom = Mathf.Max(w.Zoom <= 0f ? 1f : w.Zoom, 0.1f);

            // Cover the window at the source's own aspect, then magnify by Zoom. Solving for the inner
            // element's size rather than leaning on scale-and-crop is what makes Focus meaningful:
            // a background scale mode has no notion of WHICH part of the frame to keep.
            float coverWidth, coverHeight;
            if (windowWidth / windowHeight > aspect)
            {
                coverWidth = windowWidth;
                coverHeight = windowWidth / aspect;
            }
            else
            {
                coverHeight = windowHeight;
                coverWidth = windowHeight * aspect;
            }
            coverWidth *= zoom;
            coverHeight *= zoom;

            var inner = new VisualElement();
            inner.AddToClassList("media-window-src");
            inner.style.width = coverWidth;
            inner.style.height = coverHeight;

            // Put the focus point at the window's centre, then clamp so the crop can never slide past
            // the edge of the footage and expose the ground behind it.
            float left = windowWidth * 0.5f - coverWidth * Mathf.Clamp01(w.Focus.x);
            float top = windowHeight * 0.5f - coverHeight * Mathf.Clamp01(w.Focus.y);
            inner.style.left = Mathf.Clamp(left, windowWidth - coverWidth, 0f);
            inner.style.top = Mathf.Clamp(top, windowHeight - coverHeight, 0f);

            Paint(inner, item, media, coverWidth, ScaleMode.StretchToFill);
            frame.Add(inner);

            DrawWindowCopy(media, theme, w.Anchor, windowWidth);

            if (item.Clip != null) DrawVideoBar(theme);
        }

        /// <summary>Places the window against whichever edges its anchor names, 60 px clear of them.</summary>
        private static void AnchorWindow(VisualElement frame, MediaAnchor anchor, float width, float height)
        {
            const float Inset = 60f;

            int column = (int)anchor % 3;    // 0 left, 1 centre, 2 right
            int row = (int)anchor / 3;       // 0 top,  1 middle, 2 bottom

            switch (column)
            {
                case 0: frame.style.left = Inset; break;
                case 1: frame.style.left = (RefWidth - width) * 0.5f; break;
                default: frame.style.left = RefWidth - width - Inset; break;
            }

            switch (row)
            {
                case 0: frame.style.top = Inset; break;
                case 1: frame.style.top = (RefHeight - height) * 0.5f; break;
                default: frame.style.top = RefHeight - height - Inset; break;
            }
        }

        /// <summary>The copy takes the side the window is not on. A centred window gets no copy.</summary>
        private void DrawWindowCopy(MediaSlideSO media, PresentationThemeSO theme, MediaAnchor anchor,
                                    float windowWidth)
        {
            int column = (int)anchor % 3;
            if (column == 1) return;

            var text = new VisualElement();
            text.AddToClassList("media-window-copy");

            float free = RefWidth - windowWidth - 60f;
            if (column == 2) text.style.left = 120f;                 // window right, copy left
            else text.style.left = windowWidth + 120f;               // window left, copy right
            text.style.width = free - 180f;

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

            _host.Add(text);
        }

        // -------------------------------------------------------------------------
        // Shared pieces
        // -------------------------------------------------------------------------

        /// <summary>
        /// Puts an item's clip or still onto an element. <paramref name="displayWidth"/> is kept for the
        /// call sites' readability — it documents how wide each layout draws the item — but it no longer
        /// sizes anything, because the render texture now follows the clip's own resolution.
        /// </summary>
        private void Paint(VisualElement target, MediaItem item, MediaSlideSO media, float displayWidth,
                           ScaleMode fit)
        {
            if (item.Clip != null)
            {
                RenderTexture rt = _pool.Acquire(item.Clip, media.Loop, media.PlayAudio, media.AutoPlay);
                if (rt != null)
                {
                    target.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
                    target.style.unityBackgroundScaleMode = fit;
                }
                return;
            }

            if (item.Still != null)
            {
                target.style.backgroundImage = new StyleBackground(item.Still);
                target.style.unityBackgroundScaleMode = fit;
            }
        }

        /// <summary>Title and caption at the top of the frame — shared by Inset and Grid.</summary>
        private void DrawHeading(MediaSlideSO media, PresentationThemeSO theme)
        {
            var title = new Label(media.Title ?? string.Empty);
            title.AddToClassList("media-title");
            title.AddToClassList("media-title--inset");
            if (theme != null) title.style.color = theme.Ink;
            _host.Add(title);

            if (string.IsNullOrEmpty(media.Caption)) return;

            var cap = new Label(media.Caption);
            cap.AddToClassList("media-kicker");
            cap.AddToClassList("media-kicker--inset");
            if (theme != null) cap.style.color = theme.InkMuted;
            _host.Add(cap);
        }

        private void HideChrome()
        {
            _view.Footer?.AddToClassList("hidden");
            _view.ProgressTrack?.AddToClassList("hidden");
        }

        private void ShowChrome()
        {
            _view.Footer?.RemoveFromClassList("hidden");
            _view.ProgressTrack?.RemoveFromClassList("hidden");
            ClearBriefFade();
        }

        /// <summary>
        /// Fades the label away and leaves the clip alone. Opacity rather than display, so USS can
        /// transition it — and every element is put back by <see cref="ClearBriefFade"/>, because these
        /// are the deck's shared chrome elements and a slide that faded them must not hand them on faded.
        /// </summary>
        private void ApplyBriefFade(float seconds)
        {
            foreach (VisualElement e in BriefElements())
            {
                if (e == null) continue;
                e.style.transitionProperty = new List<StylePropertyName> { "opacity" };
                e.style.transitionDuration = new List<TimeValue> { new TimeValue(seconds, TimeUnit.Second) };
                e.style.opacity = 0f;
            }
        }

        private void ClearBriefFade()
        {
            foreach (VisualElement e in BriefElements())
            {
                if (e == null) continue;
                e.style.transitionProperty = new StyleList<StylePropertyName>(StyleKeyword.Null);
                e.style.transitionDuration = new StyleList<TimeValue>(StyleKeyword.Null);
                e.style.opacity = new StyleFloat(StyleKeyword.Null);
            }
        }

        private IEnumerable<VisualElement> BriefElements()
        {
            yield return _view.Scrim;
            yield return _view.Lockup;
            yield return _view.Footer;
            yield return _view.ProgressTrack;
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

        /// <summary>
        /// The transport bar, for single-clip layouts only. A grid would need one per tile, and four
        /// progress rails is noise rather than information.
        /// </summary>
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

        /// <summary>Pause or resume everything on this slide. Bound to P by the input relay.</summary>
        public void TogglePause() => _pool?.TogglePause();

        private void Update()
        {
            if (_pool == null) return;

            // The brief expiring is a one-shot: the fade itself is USS's job from there.
            if (!_briefFaded && _current != null && Time.unscaledTime >= _briefEndsAt)
            {
                _briefFaded = true;
                ApplyBriefFade(_current.BriefFadeSeconds);
            }

            VideoPlayer player = _pool.Primary;
            bool playing = _pool.AnyPlaying;

            // Theatre keeps its own indicator, and has no transport bar to update.
            if (_theatrePause != null) _theatrePause.EnableInClassList("hidden", playing);

            if (player == null || player.clip == null || _fill == null) return;

            double length = player.length;
            double time = player.time;
            float t = length > 0.01 ? Mathf.Clamp01((float)(time / length)) : 0f;
            _fill.style.width = new Length(t * 100f, LengthUnit.Percent);

            if (_timeLabel != null)
                _timeLabel.text = Format(time) + " / " + Format(length);

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

        private void OnDestroy() => _pool?.Dispose();
    }
}
