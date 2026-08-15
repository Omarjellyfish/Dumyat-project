using UnityEngine;
using UnityEngine.Video;

namespace DumyatRuntime
{
    /// <summary>
    /// One thing shown on a media slide. A slide with two of these is a 2-up; with one it is whatever
    /// single-item layout the slide asks for.
    ///
    /// Clip and Still are alternatives, not a pair: a clip wins when both are set, exactly as the old
    /// single-clip field did. <see cref="Aspect"/> is the whole reason this is a struct rather than a
    /// bare array of clips — knowing each item's shape at author time is what lets the grid size tiles
    /// so that nothing is cropped, letterboxed or stretched.
    /// </summary>
    [System.Serializable]
    public struct MediaItem
    {
        [Tooltip("Footage. Takes priority over the still when both are set.")]
        public VideoClip Clip;

        [Tooltip("Used when Clip is empty.")]
        public Sprite Still;

        [Tooltip("Small uppercase label on this tile. Optional, and only drawn by the Grid layout.")]
        public string Caption;

        public bool HasContent => Clip != null || Still != null;

        /// <summary>
        /// Width over height of the source. Falls back to 16:9 — an unknown aspect drawn as 16:9 is the
        /// least wrong guess for game capture, and the item has no content to distort anyway.
        /// </summary>
        public float Aspect
        {
            get
            {
                if (Clip != null && Clip.height > 0) return Clip.width / (float)Clip.height;
                if (Still != null && Still.rect.height > 0.01f) return Still.rect.width / Still.rect.height;
                return 16f / 9f;
            }
        }
    }

    /// <summary>Nine-point placement of a windowed crop on the slide.</summary>
    public enum MediaAnchor
    {
        TopLeft = 0, TopCenter = 1, TopRight = 2,
        MiddleLeft = 3, MiddleCenter = 4, MiddleRight = 5,
        BottomLeft = 6, BottomCenter = 7, BottomRight = 8,
    }

    /// <summary>What theatre mode does with a clip whose aspect is not the projector's.</summary>
    public enum TheatreFit
    {
        /// <summary>Cover the screen and crop the overhang. No bars, but the edges are lost.</summary>
        Fill = 0,

        /// <summary>Show the whole frame and pad with the deck ground. Bars, but nothing is lost.</summary>
        Fit = 1,
    }

    /// <summary>
    /// A deliberately cropped window of one clip, placed somewhere on the slide with the copy taking
    /// the space it leaves.
    ///
    /// <see cref="Focus"/> is in SOURCE space and <see cref="Anchor"/> is in SLIDE space — they are two
    /// different questions ("which part of the footage" and "where on screen") and conflating them is
    /// what makes crop tools frustrating.
    /// </summary>
    [System.Serializable]
    public struct MediaWindow
    {
        [Tooltip("Which point of the SOURCE frame to keep centred in the window. 0,0 is its top-left, " +
                 "0.5,0.5 the middle. Clamped so the crop can never show past the edge of the footage.")]
        public Vector2 Focus;

        [Tooltip("1 = the window is filled by the frame with no extra magnification. 2 = a half-frame " +
                 "crop, so half the detail at twice the size.")]
        public float Zoom;

        [Tooltip("Where the window sits on the slide.")]
        public MediaAnchor Anchor;

        [Tooltip("Window size as a fraction of the slide. 0.36 x 0.82 is a tall panel down one side; " +
                 "0.9 x 0.3 is a letterbox strip.")]
        public Vector2 Size;

        [Tooltip("Corner radius in reference pixels. Delta's panel radius is 22.")]
        public float Corner;

        /// <summary>Sane values for a fresh asset — a tall right-hand window at no extra zoom.</summary>
        public static MediaWindow Default => new MediaWindow
        {
            Focus = new Vector2(0.5f, 0.5f),
            Zoom = 1f,
            Anchor = MediaAnchor.MiddleRight,
            Size = new Vector2(0.36f, 0.82f),
            Corner = 22f,
        };
    }

    /// <summary>
    /// A slide carrying footage or stills. One asset type covers all of Mechanics and Sprint
    /// Overview — thirteen slides off one class, which is the point of the layout enum.
    ///
    /// WHY AN UNASSIGNED CLIP IS A FIRST-CLASS STATE:
    /// none of the footage exists yet. A slide with no media renders a capture card carrying its own
    /// brief instead of an empty frame, so the deck is presentable today and doubles as the shot list.
    /// Dropping a clip in later turns the card into the real slide with no other change.
    /// </summary>
    [CreateAssetMenu(fileName = "Slide_Media", menuName = "Dumyat/Presentation/Media Slide")]
    public sealed class MediaSlideSO : SlideSO
    {
        [Header("Layout")]
        [SerializeField] private MediaSlideLayout _layout = MediaSlideLayout.FullBleed;

        [Header("Media — leave empty for a capture card")]
        [SerializeField, Tooltip("What this slide shows, in reading order. One entry for the " +
                                 "single-item layouts; two to six for Grid.")]
        private MediaItem[] _items = new MediaItem[0];

        [Header("Grid")]
        [SerializeField, Range(0, 4), Tooltip("Columns. 0 chooses for you: 2 items across, 3 across, " +
                                              "4 as a 2x2, 5–6 as two rows of three.")]
        private int _gridColumns;

        [Header("Theatre")]
        [SerializeField, Tooltip("Fill crops to cover the screen; Fit shows the whole frame with bars.")]
        private TheatreFit _theatreFit = TheatreFit.Fill;

        [Header("Window")]
        [SerializeField] private MediaWindow _window = MediaWindow.Default;

        [Header("Playback")]
        [SerializeField] private bool _autoPlay = true;

        [SerializeField, Tooltip("Loop until the presenter advances. Off holds the last frame — which " +
                                 "never auto-advances, because the presenter always owns the advance.")]
        private bool _loop = true;

        [SerializeField, Tooltip("Play the clip's own audio. Off for anything shown while talking over " +
                                 "it.\n\nIgnored when the slide has more than one clip — several " +
                                 "soundtracks at once is never what was wanted.")]
        private bool _playAudio;

        [Header("Brief — full-bleed video only")]
        [SerializeField, Range(0f, 20f),
         Tooltip("Seconds the title, caption and deck chrome stay up before fading away, leaving the " +
                 "clip playing on its own.\n\nThe slide introduces itself, then gets out of the way — " +
                 "so a video can be both labelled AND full screen, instead of choosing one. 0 keeps the " +
                 "chrome up for the whole clip.")]
        private float _briefSeconds = 4f;

        [SerializeField, Range(0.1f, 3f), Tooltip("How long the fade itself takes.")]
        private float _briefFadeSeconds = 0.6f;

        [Header("Caption")]
        [SerializeField, TextArea(2, 4), Tooltip("Shown in the lockup. Falls back to the slide Kicker.")]
        private string _caption;

        [Header("Capture card — used only while media is missing")]
        [SerializeField, TextArea(2, 5),
         Tooltip("What still needs shooting. This is the shot list: say what to record, where, and " +
                 "roughly how long.")]
        private string _captureBrief;

        [SerializeField, Tooltip("Rough target length, shown on the card. Optional.")]
        private string _captureLength;

        public MediaSlideLayout Layout => _layout;
        public MediaItem[] Items => _items;
        public int GridColumns => _gridColumns;
        public TheatreFit TheatreFit => _theatreFit;
        public MediaWindow Window => _window;
        public bool AutoPlay => _autoPlay;

        /// <summary>Seconds of chrome before it fades. 0 means it never does.</summary>
        public float BriefSeconds => Mathf.Max(0f, _briefSeconds);

        public float BriefFadeSeconds => Mathf.Clamp(_briefFadeSeconds, 0.1f, 3f);
        public bool Loop => _loop;
        public string Caption => string.IsNullOrEmpty(_caption) ? Kicker : _caption;
        public string CaptureBrief => _captureBrief;
        public string CaptureLength => _captureLength;

        /// <summary>The first item with anything in it — what every single-item layout draws.</summary>
        public MediaItem Primary
        {
            get
            {
                if (_items != null)
                {
                    foreach (MediaItem item in _items)
                        if (item.HasContent) return item;
                }
                return default;
            }
        }

        /// <summary>How many items actually carry a clip. Drives the audio rule and the decoder budget.</summary>
        public int ClipCount
        {
            get
            {
                int n = 0;
                if (_items != null)
                {
                    foreach (MediaItem item in _items)
                        if (item.Clip != null) n++;
                }
                return n;
            }
        }

        /// <summary>
        /// Audio only ever comes from a slide showing ONE clip. Four tiles each playing their own
        /// soundtrack is not a thing anyone wants, and muting all but one silently would be a worse
        /// surprise than the rule being stated.
        /// </summary>
        public bool PlayAudio => _playAudio && ClipCount == 1;

        /// <summary>True when there is nothing to show yet and the capture card stands in.</summary>
        public bool IsPending
        {
            get
            {
                if (_items == null) return true;
                foreach (MediaItem item in _items)
                    if (item.HasContent) return false;
                return true;
            }
        }

        /// <summary>
        /// A pending slide keeps the backdrop — the capture card is type on a ground, not media.
        /// Once footage lands, the layouts that cover the frame themselves make the backdrop wasted
        /// work; the ones that leave the slide's own ground showing still need it.
        /// </summary>
        public override bool WantsBackdrop =>
            IsPending || (_layout != MediaSlideLayout.FullBleed && _layout != MediaSlideLayout.Theatre);
    }

    public enum MediaSlideLayout
    {
        /// <summary>Media fills the frame; scrim and lockup sit over it.</summary>
        FullBleed = 0,

        /// <summary>Title and body on the left, media on the right.</summary>
        SideBySide = 1,

        /// <summary>Title above a row of stills. For a sprint slide showing several moments.</summary>
        Inset = 2,

        /// <summary>
        /// Two to six items, each tile sized to its own aspect so nothing is cropped or letterboxed.
        /// </summary>
        Grid = 3,

        /// <summary>One clip, the entire screen, no chrome at all.</summary>
        Theatre = 4,

        /// <summary>One clip, cropped and anchored, with the copy taking the rest of the slide.</summary>
        Window = 5,
    }
}
