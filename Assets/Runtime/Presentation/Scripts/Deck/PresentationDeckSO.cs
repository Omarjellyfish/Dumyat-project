using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The deck: an ordered list of slides plus the defaults each slide inherits when it does not
    /// override them. Authoring a presentation is filling this in; a second asset gives a shorter
    /// cut of the same slides for nothing.
    /// </summary>
    [CreateAssetMenu(fileName = "PresentationDeck", menuName = "Dumyat/Presentation/Deck")]
    public sealed class PresentationDeckSO : ScriptableObject
    {
        [Header("Slides, in presentation order")]
        [SerializeField] private List<SlideSO> _slides = new List<SlideSO>();

        [Header("Defaults — a slide's own override wins over these")]
        [SerializeField] private PresentationThemeSO _theme;
        [SerializeField] private SlideBackgroundSO _defaultBackground;
        [SerializeField] private SlideTransitionSO _defaultTransition;

        [Header("Sections")]
        [SerializeField, Tooltip("Display names indexed by section number. Entry 0 is unused so the " +
                                 "numbers line up with the number keys and the contents page.")]
        private string[] _sectionNames = new string[0];

        [Header("Residency")]
        [SerializeField, Tooltip("Phase 0 measured all six scenes resident at 1696 MB of a ~3400 MB " +
                                 "budget, so AllResidentAtBoot is viable on the presenting machine. " +
                                 "LookaheadWindow exists as insurance for weaker hardware.")]
        private StageResidencyPolicy _residency = StageResidencyPolicy.AllResidentAtBoot;

        public IReadOnlyList<SlideSO> Slides => _slides;
        public PresentationThemeSO Theme => _theme;
        public SlideBackgroundSO DefaultBackground => _defaultBackground;
        public SlideTransitionSO DefaultTransition => _defaultTransition;
        public StageResidencyPolicy Residency => _residency;

        public int Count => _slides.Count;

        /// <summary>Null-safe indexer — a hole in the list must not take the whole deck down mid-talk.</summary>
        public SlideSO SlideAt(int index) =>
            index >= 0 && index < _slides.Count ? _slides[index] : null;

        /// <summary>The background a slide should actually use, following its override then the default.</summary>
        public SlideBackgroundSO ResolveBackground(SlideSO slide) =>
            slide != null && slide.Background != null ? slide.Background : _defaultBackground;

        /// <summary>The transition a slide should actually arrive with.</summary>
        public SlideTransitionSO ResolveTransition(SlideSO slide) =>
            slide != null && slide.TransitionIn != null ? slide.TransitionIn : _defaultTransition;

        /// <summary>
        /// Declared name of a section, straight from the deck's table and independent of whether any
        /// slide claims that number. The contents page needs this to list sections, and the deck
        /// validator needs it to spot a section that is named but has no slides.
        /// </summary>
        public string SectionNameAt(int sectionNumber) =>
            sectionNumber >= 0 && sectionNumber < _sectionNames.Length
                ? _sectionNames[sectionNumber]
                : string.Empty;

        /// <summary>Highest section number the deck's table declares a name for.</summary>
        public int DeclaredSectionCount => _sectionNames.Length;

        /// <summary>Section name for the eyebrow, following the slide's override then the deck table.</summary>
        public string ResolveSectionName(SlideSO slide)
        {
            if (slide == null) return string.Empty;
            if (!string.IsNullOrEmpty(slide.SectionName)) return slide.SectionName;

            int n = slide.SectionNumber;
            return n >= 0 && n < _sectionNames.Length ? _sectionNames[n] : string.Empty;
        }

        /// <summary>
        /// Index of the first slide in a section, or -1 if that section is not in the deck. Used by
        /// the number-key jump; returning -1 rather than clamping is what makes a mistyped key a
        /// no-op instead of a surprise.
        /// </summary>
        public int FirstSlideOfSection(int sectionNumber)
        {
            for (int i = 0; i < _slides.Count; i++)
            {
                if (_slides[i] != null && _slides[i].SectionNumber == sectionNumber) return i;
            }
            return -1;
        }
    }

    public enum StageResidencyPolicy
    {
        /// <summary>Every demo scene loads at boot and stays. Measured viable in Phase 0.</summary>
        AllResidentAtBoot = 0,

        /// <summary>Only the previous, current and next stages stay loaded. Insurance for less VRAM.</summary>
        LookaheadWindow = 1,
    }
}
