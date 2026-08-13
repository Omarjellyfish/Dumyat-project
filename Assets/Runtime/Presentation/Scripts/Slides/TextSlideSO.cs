using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// A slide made only of type. One asset type covers the contents page, the story pages, all six
    /// section dividers, future plans and the closing slide — the <see cref="TextSlideLayout"/> enum
    /// is what differs, not the component.
    /// </summary>
    [CreateAssetMenu(fileName = "Slide_Text", menuName = "Dumyat/Presentation/Text Slide")]
    public sealed class TextSlideSO : SlideSO
    {
        [Header("Layout")]
        [SerializeField, Tooltip("Which arrangement of the shared slide tree to show.")]
        private TextSlideLayout _layout = TextSlideLayout.TitleAndBody;

        [SerializeField, Tooltip("Poster treatment: oversized title, and a one-off reveal that raises " +
                                 "the title, then the kicker, then draws a hairline across.\n\n" +
                                 "Meant for the opening slide only. Everything else in the deck is " +
                                 "still on purpose — that is what makes one moving slide land.")]
        private bool _posterTreatment;

        [Header("Body")]
        [SerializeField, TextArea(3, 10),
         Tooltip("Prose paragraph. Used by TitleAndBody. Ignored by the other layouts.")]
        private string _body;

        [SerializeField, Tooltip("One entry per row. Used by Bullets and TwoColumn. " +
                                 "TwoColumn splits the list down the middle.")]
        private string[] _bullets = new string[0];

        [Header("Readouts — optional, right-aligned figures")]
        [SerializeField, Tooltip("Small uppercase label above each value. Delta shows data as " +
                                 "label-plus-number, never as a pill.")]
        private SlideReadout[] _readouts = new SlideReadout[0];

        public TextSlideLayout Layout => _layout;
        public bool PosterTreatment => _posterTreatment;
        public string Body => _body;
        public System.Collections.Generic.IReadOnlyList<string> Bullets => _bullets;
        public System.Collections.Generic.IReadOnlyList<SlideReadout> Readouts => _readouts;
    }

    public enum TextSlideLayout
    {
        /// <summary>Title alone, vertically centred.</summary>
        TitleOnly = 0,

        /// <summary>Big outlined section numeral, section word, title, kicker. Opens each section.</summary>
        SectionDivider = 1,

        /// <summary>Eyebrow, hairline, title, kicker, one prose paragraph.</summary>
        TitleAndBody = 2,

        /// <summary>Eyebrow, hairline, title, then hairline-separated numbered rows.</summary>
        Bullets = 3,

        /// <summary>As Bullets, but the list is split into two columns. Doubles as the contents page.</summary>
        TwoColumn = 4,
    }

    /// <summary>A label-and-value pair shown as a right-aligned figure rather than a badge.</summary>
    [System.Serializable]
    public struct SlideReadout
    {
        [Tooltip("Small uppercase label, e.g. \"Scenes\".")]
        public string Label;

        [Tooltip("The figure itself, e.g. \"18\" or \"106 MB\".")]
        public string Value;

        [Tooltip("Tint the value jade, for a positive or headline figure.")]
        public bool Positive;
    }
}
