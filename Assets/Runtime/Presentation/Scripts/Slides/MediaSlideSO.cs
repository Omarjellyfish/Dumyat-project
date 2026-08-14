using UnityEngine;
using UnityEngine.Video;

namespace DumyatRuntime
{
    /// <summary>
    /// A slide carrying footage or stills. One asset type covers all of Mechanics and Sprint
    /// Overview — fifteen slides off one class, which is the point of the layout enum.
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
        [SerializeField, Tooltip("Footage. Takes priority over stills when both are set.")]
        private VideoClip _clip;

        [SerializeField, Tooltip("Stills. FullBleed and SideBySide use the first; Inset uses up to four.")]
        private Sprite[] _stills = new Sprite[0];

        [Header("Playback")]
        [SerializeField] private bool _autoPlay = true;

        [SerializeField, Tooltip("Loop until the presenter advances. Off holds the last frame — which " +
                                 "never auto-advances, because the presenter always owns the advance.")]
        private bool _loop = true;

        [SerializeField, Tooltip("Play the clip's own audio. Off for anything shown while talking over it.")]
        private bool _playAudio;

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
        public VideoClip Clip => _clip;
        public Sprite[] Stills => _stills;
        public bool AutoPlay => _autoPlay;
        public bool Loop => _loop;
        public bool PlayAudio => _playAudio;
        public string Caption => string.IsNullOrEmpty(_caption) ? Kicker : _caption;
        public string CaptureBrief => _captureBrief;
        public string CaptureLength => _captureLength;

        /// <summary>True when there is nothing to show yet and the capture card stands in.</summary>
        public bool IsPending => _clip == null && (_stills == null || _stills.Length == 0 || _stills[0] == null);

        /// <summary>
        /// A pending slide keeps the backdrop — the capture card is type on a ground, not media.
        /// Once footage lands, FullBleed media covers the frame itself and the backdrop is wasted work.
        /// </summary>
        public override bool WantsBackdrop => IsPending || _layout != MediaSlideLayout.FullBleed;
    }

    public enum MediaSlideLayout
    {
        /// <summary>Media fills the frame; scrim and lockup sit over it.</summary>
        FullBleed = 0,

        /// <summary>Title and body on the left, media on the right.</summary>
        SideBySide = 1,

        /// <summary>Title above a row of stills. For a sprint slide showing several moments.</summary>
        Inset = 2,
    }
}
