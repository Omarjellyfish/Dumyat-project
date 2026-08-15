using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Two recordings of the same thing, played one at a time against a live visualiser — the shape a
    /// before/after comparison wants when the difference is audible rather than visible.
    ///
    /// WHY THE CLIPS ARE PLAYED RATHER THAN DRAWN:
    /// a static waveform proves very little about a voice. The room judges a TTS change with its ears,
    /// so the slide's job is to get both clips heard cleanly, in order, without the presenter leaving
    /// the deck to find an audio player. The visualiser exists to show WHICH clip is speaking and how
    /// far through it is — not to argue that one is better.
    ///
    /// Only one lane ever sounds. Two voices at once tells the room nothing, so starting either clip
    /// stops the other.
    /// </summary>
    [CreateAssetMenu(fileName = "Slide_AudioCompare", menuName = "Dumyat/Presentation/Audio Compare Slide")]
    public sealed class AudioCompareSlideSO : SlideSO
    {
        [Header("Before")]
        [SerializeField] private AudioClip _beforeClip;

        [SerializeField, Tooltip("Authored in the case it should display in — USS has no text-transform.")]
        private string _beforeLabel = "BEFORE";

        [Header("After")]
        [SerializeField] private AudioClip _afterClip;

        [SerializeField] private string _afterLabel = "AFTER";

        [Header("Visualiser")]
        [SerializeField, Range(16, 96),
         Tooltip("Bars across each lane. 48 is what the design was drawn at — enough to read as a voice, " +
                 "few enough that each bar is still a visible width on a 2K screen.")]
        private int _barCount = 48;

        [SerializeField, Range(1f, 30f),
         Tooltip("How fast a bar eases toward its new height, in units per second. This is the whole " +
                 "difference between a visualiser that looks designed and one that looks like noise — " +
                 "raw per-frame spectrum values flicker badly.")]
        private float _smoothing = 12f;

        [SerializeField, Range(0.5f, 8f),
         Tooltip("Multiplies the spectrum before it becomes a height. Speech sits low in the range, so " +
                 "without a boost the bars barely leave the floor.")]
        private float _gain = 3.2f;

        public AudioClip BeforeClip => _beforeClip;
        public AudioClip AfterClip => _afterClip;
        public string BeforeLabel => string.IsNullOrEmpty(_beforeLabel) ? "BEFORE" : _beforeLabel;
        public string AfterLabel => string.IsNullOrEmpty(_afterLabel) ? "AFTER" : _afterLabel;
        public int BarCount => Mathf.Clamp(_barCount, 16, 96);
        public float Smoothing => Mathf.Clamp(_smoothing, 1f, 30f);
        public float Gain => Mathf.Clamp(_gain, 0.5f, 8f);

        /// <summary>The lanes sit on the deck's own ground, so the backdrop stays.</summary>
        public override bool WantsBackdrop => true;
    }
}
