using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Owns the deck's <see cref="UIDocument"/> and resolves the named elements once.
    ///
    /// WHY THIS EXISTS RATHER THAN EVERY COMPONENT CALLING Q():
    /// four separate components need parts of this tree. If each queried it themselves, the element
    /// names would be duplicated four times and a UXML rename would break them one at a time, at
    /// runtime, silently. Here the names appear exactly once and a missing element is reported at
    /// startup with the name that is wrong.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PresentationView : MonoBehaviour
    {
        private UIDocument _document;

        public VisualElement Root { get; private set; }

        /// <summary>
        /// The `presentation-root` element — NOT the same thing as <see cref="Root"/>.
        ///
        /// Root is the UIDocument's own container, which is always transparent. The opaque Delta ground
        /// lives one level down, on this element, because USS puts `background-color` there. Anything
        /// that needs to see through the whole UI to the 3D behind it has to clear THIS one; clearing
        /// Root looks correct, compiles, runs, and does absolutely nothing — which is precisely how
        /// every scene slide came to render as chrome over a flat blue-black rectangle.
        /// </summary>
        public VisualElement Ground { get; private set; }

        public VisualElement Backdrop { get; private set; }
        public VisualElement Content { get; private set; }
        public VisualElement Cover { get; private set; }

        public VisualElement TextSlide { get; private set; }
        public Label DividerNumeral { get; private set; }
        public VisualElement EyebrowRow { get; private set; }
        public Label EyebrowLabel { get; private set; }
        public VisualElement EyebrowDot { get; private set; }
        public VisualElement Rule { get; private set; }
        public Label Title { get; private set; }
        public Label Kicker { get; private set; }
        public Label Body { get; private set; }
        public VisualElement Bullets { get; private set; }
        public VisualElement BulletsTwo { get; private set; }
        public VisualElement BulletsLeft { get; private set; }
        public VisualElement BulletsRight { get; private set; }
        public VisualElement Readouts { get; private set; }

        // Scene slides — scrim, lockup and angle pips instead of a panel ground.
        public VisualElement Scrim { get; private set; }
        public VisualElement Lockup { get; private set; }
        public VisualElement LockupRule { get; private set; }
        public Label LockupPlace { get; private set; }
        public Label LockupArabic { get; private set; }
        public Label LockupCaption { get; private set; }
        public VisualElement Pips { get; private set; }
        public Label PipsLabel { get; private set; }
        public VisualElement PipsRow { get; private set; }

        public Label FooterLeft { get; private set; }
        public Label FooterCounter { get; private set; }
        public VisualElement ProgressFill { get; private set; }
        public VisualElement NotesPanel { get; private set; }
        public Label NotesText { get; private set; }

        /// <summary>False when any element failed to resolve; every consumer null-guards on this.</summary>
        public bool IsReady { get; private set; }

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            Resolve();
        }

        private void Resolve()
        {
            if (_document == null || _document.rootVisualElement == null)
            {
                Debug.LogWarning("[PresentationView] No UIDocument root — is a Source Asset and " +
                                 "Panel Settings assigned?", this);
                return;
            }

            Root = _document.rootVisualElement;
            int missing = 0;

            Ground   = Need<VisualElement>("presentation-root", ref missing);
            Backdrop = Need<VisualElement>("backdrop", ref missing);
            Content  = Need<VisualElement>("content", ref missing);
            Cover    = Need<VisualElement>("cover", ref missing);

            TextSlide      = Need<VisualElement>("text-slide", ref missing);
            DividerNumeral = Need<Label>("divider-numeral", ref missing);
            EyebrowRow     = Need<VisualElement>("eyebrow-row", ref missing);
            EyebrowLabel   = Need<Label>("eyebrow-label", ref missing);
            EyebrowDot     = Need<VisualElement>("eyebrow-dot", ref missing);
            Rule           = Need<VisualElement>("rule", ref missing);
            Title          = Need<Label>("title", ref missing);
            Kicker         = Need<Label>("kicker", ref missing);
            Body           = Need<Label>("body", ref missing);
            Bullets        = Need<VisualElement>("bullets", ref missing);
            BulletsTwo     = Need<VisualElement>("bullets-two", ref missing);
            BulletsLeft    = Need<VisualElement>("bullets-left", ref missing);
            BulletsRight   = Need<VisualElement>("bullets-right", ref missing);
            Readouts       = Need<VisualElement>("readouts", ref missing);

            Scrim         = Need<VisualElement>("scrim", ref missing);
            Lockup        = Need<VisualElement>("lockup", ref missing);
            LockupRule    = Need<VisualElement>("lockup-rule", ref missing);
            LockupPlace   = Need<Label>("lockup-place", ref missing);
            LockupArabic  = Need<Label>("lockup-arabic", ref missing);
            LockupCaption = Need<Label>("lockup-caption", ref missing);
            Pips          = Need<VisualElement>("pips", ref missing);
            PipsLabel     = Need<Label>("pips-label", ref missing);
            PipsRow       = Need<VisualElement>("pips-row", ref missing);

            FooterLeft    = Need<Label>("footer-left", ref missing);
            FooterCounter = Need<Label>("footer-counter", ref missing);
            ProgressFill  = Need<VisualElement>("progress-fill", ref missing);
            NotesPanel    = Need<VisualElement>("notes-panel", ref missing);
            NotesText     = Need<Label>("notes-text", ref missing);

            IsReady = missing == 0;
            if (!IsReady)
                Debug.LogWarning($"[PresentationView] {missing} element(s) missing from " +
                                 "PresentationSlide.uxml — the deck will run but parts will not update.", this);
        }

        private T Need<T>(string elementName, ref int missing) where T : VisualElement
        {
            T found = Root.Q<T>(elementName);
            if (found == null)
            {
                Debug.LogWarning($"[PresentationView] element '{elementName}' not found.", this);
                missing++;
            }
            return found;
        }
    }
}
