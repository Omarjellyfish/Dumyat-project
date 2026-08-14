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
    /// <summary>
    /// Effects that lift a title off whatever sits behind it. Tick any combination.
    ///
    /// WHY THIS IS FLAGS EVEN THOUGH USS ALLOWS ONE text-shadow:
    /// <c>StyleTextShadow</c> holds exactly one <c>TextShadow</c>, so Deep and Halo genuinely cannot
    /// both live on one label. Rather than forbid the combination, <see cref="TitleEmphasis"/> puts the
    /// second shadow on a matching label tucked inside the first — the only route to a layered shadow
    /// in this engine, and invisible from here. Outline is a different property and always stacks freely.
    /// </summary>
    [System.Flags]
    public enum TitleEffects
    {
        None = 0,

        /// <summary>Soft, wide and dark. The deck's existing idiom — the scene lockups use it.</summary>
        DeepShadow = 1 << 0,

        /// <summary>A bloom in the accent cyan. Striking on dark artwork, fights a warm image.</summary>
        Halo = 1 << 1,

        /// <summary>A dark hairline around the letterforms. The only one that holds over a LIGHT
        /// background as well as a dark one, because it is an edge rather than a darkening.</summary>
        Outline = 1 << 2,
    }

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

        [Header("Title emphasis — for a title sitting over artwork or live 3D")]
        [SerializeField, Tooltip("Tick any combination.\n\nDeep Shadow + Outline is the pairing that " +
                                 "survives any artwork — an edge that holds over light and dark, with " +
                                 "depth underneath. Halo ties the title to the deck's accent cyan and " +
                                 "is strongest on dark images.\n\nDeep Shadow and Halo together are " +
                                 "drawn on two stacked labels, because USS allows only one text-shadow " +
                                 "per element. Nothing to do — it is handled for you.")]
        private TitleEffects _titleEffects = TitleEffects.None;

        [SerializeField, Tooltip("Same effects, applied to the kicker line under the title.\n\n" +
                                 "Usually lighter than the title's: the kicker is small enough that a " +
                                 "heavy outline closes up its letterforms, so it is drawn at 1 px " +
                                 "against the title's 2–3 px.")]
        private TitleEffects _kickerEffects = TitleEffects.None;

        [Header("Overrides — leave empty to use the deck default")]
        [SerializeField] private SlideBackgroundSO _background;
        [SerializeField] private SlideTransitionSO _transitionIn;

        public int SectionNumber => _sectionNumber;
        public string SectionName => _sectionName;
        public string Title => _title;
        public string Kicker => _kicker;
        public string SpeakerNotes => _speakerNotes;

        /// <summary>Emphasis effects on this slide's title. See <see cref="TitleEmphasis"/>.</summary>
        public TitleEffects Effects => _titleEffects;

        /// <summary>Emphasis effects on this slide's kicker line.</summary>
        public TitleEffects KickerEffects => _kickerEffects;

        /// <summary>Convenience for the common test.</summary>
        public bool Has(TitleEffects effect) => (_titleEffects & effect) != 0;

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
