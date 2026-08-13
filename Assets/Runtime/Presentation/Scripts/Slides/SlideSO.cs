using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Base of every slide asset. Holds only what EVERY slide has; anything type-specific belongs on
    /// a subclass. Adding a new kind of slide is one subclass plus one <see cref="ISlideRenderer"/>,
    /// with nothing existing edited.
    ///
    /// Empty overrides fall back to the deck defaults — see <see cref="PresentationDeckSO"/> — so a
    /// typical slide asset only fills in its text.
    /// </summary>
    public abstract class SlideSO : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField, Tooltip("Section this slide belongs to, 1-based, matching the number keys. " +
                                 "The first slide of each section is what a number-key jump lands on.")]
        private int _sectionNumber = 1;

        [SerializeField, Tooltip("Section name shown in the eyebrow, e.g. \"Art Showcase\". " +
                                 "Leave empty to inherit the section name from the deck.")]
        private string _sectionName;

        [Header("Copy")]
        [SerializeField, Tooltip("Main heading. Authored in the case it should display in — " +
                                 "UI Toolkit has no text-transform.")]
        private string _title;

        [SerializeField, Tooltip("One muted line under the title. Optional.")]
        private string _kicker;

        [SerializeField, TextArea(3, 8), Tooltip("Speaker notes. Never shown on the projector; " +
                                                 "toggled on the presenter's screen with N.")]
        private string _speakerNotes;

        [Header("Overrides — leave empty to use the deck default")]
        [SerializeField] private SlideBackgroundSO _background;
        [SerializeField] private SlideTransitionSO _transitionIn;

        public int SectionNumber => _sectionNumber;
        public string SectionName => _sectionName;
        public string Title => _title;
        public string Kicker => _kicker;
        public string SpeakerNotes => _speakerNotes;

        /// <summary>Background override, or null to use the deck default.</summary>
        public SlideBackgroundSO Background => _background;

        /// <summary>Transition played when arriving at this slide, or null for the deck default.</summary>
        public SlideTransitionSO TransitionIn => _transitionIn;

        /// <summary>
        /// Whether this slide wants the backdrop layer painted behind it.
        ///
        /// Scene slides say no: the backdrop is an opaque full-screen element, and leaving it on would
        /// cover the 3D render the slide exists to show. Overriding this rather than relying on an
        /// unassigned background field means the deck default cannot accidentally reintroduce it.
        /// </summary>
        public virtual bool WantsBackdrop => true;

        /// <summary>
        /// What the LocationSlate transition writes on the covered screen while arriving at this slide.
        ///
        /// It lives on the slide rather than being dug out by the transition player because only the
        /// slide knows what it is: a scene slide wants its place name, a text slide its title. That keeps
        /// the director and the player free of any knowledge of slide types, which is the whole reason
        /// adding a slide kind has never meant editing either of them.
        /// </summary>
        public virtual string SlateTitle => _title;

        /// <summary>Second slate line, under the title. Null or empty hides it.</summary>
        public virtual string SlateSubtitle => null;
    }
}
