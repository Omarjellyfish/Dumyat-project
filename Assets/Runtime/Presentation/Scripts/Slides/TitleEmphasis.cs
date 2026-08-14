using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Puts a slide's title emphasis onto a label, and takes it off again.
    ///
    /// WHY THIS IS SHARED RATHER THAN A FEW LINES IN THE TEXT RENDERER:
    /// the deck has four labels that can end up carrying a title over something busy — the text slide's
    /// <c>#title</c>, the media slide's <c>.media-title</c>, the lockup's <c>#lockup-place</c>, and the
    /// transition slate's <c>#slate-title</c>. The moment a second one wants an outline, the class names,
    /// the clear-before-apply rule and the stacked-shadow trick below would exist in two places and
    /// drift. One implementation, and the USS classes are unscoped so they drop onto any of them.
    ///
    /// CLEARING IS NOT OPTIONAL. These labels are reused for every slide rather than rebuilt, so a class
    /// left on by slide 1 is still on for slide 2 — an outline would silently spread through the whole
    /// deck from a single opted-in poster. <see cref="Apply"/> always clears first for that reason.
    /// </summary>
    public static class TitleEmphasis
    {
        public const string ShadowDeepClass = "title-shadow-deep";
        public const string ShadowHaloClass = "title-shadow-halo";
        public const string OutlineClass = "title-outline";
        public const string EchoClass = "title-echo";

        private const string EchoName = "title-echo";

        /// <summary>
        /// Applies <paramref name="fx"/> to <paramref name="label"/>, replacing whatever the previous
        /// slide left behind. Null-safe.
        ///
        /// Takes the effects rather than the slide because more than one label on a slide wants them —
        /// the title and the kicker carry their own sets, and a lockup or a slate could later carry a
        /// third without this needing to know which field it is reading.
        /// </summary>
        public static void Apply(Label label, TitleEffects fx)
        {
            if (label == null) return;

            bool deep = (fx & TitleEffects.DeepShadow) != 0;
            bool halo = (fx & TitleEffects.Halo) != 0;
            bool outline = (fx & TitleEffects.Outline) != 0;

            // Outline is its own property, so it never competes with a shadow.
            label.EnableInClassList(OutlineClass, outline);

            // EnableInClassList rather than Add/Remove: it is idempotent, so a repaint of the same slide
            // costs nothing and there is no "was it already on?" state to track.
            //
            // The front label takes the deep shadow when there is one; the halo only lands here when it
            // is the ONLY shadow asked for. When both are ticked the halo moves to the echo below.
            label.EnableInClassList(ShadowDeepClass, deep);
            label.EnableInClassList(ShadowHaloClass, halo && !deep);

            SyncEcho(label, wanted: deep && halo);
        }

        /// <summary>Strips every emphasis class and the echo. For a renderer hiding its label entirely.</summary>
        public static void Clear(Label label)
        {
            if (label == null) return;

            label.RemoveFromClassList(ShadowDeepClass);
            label.RemoveFromClassList(ShadowHaloClass);
            label.RemoveFromClassList(OutlineClass);
            SyncEcho(label, wanted: false);
        }

        /// <summary>
        /// The second shadow, on a duplicate label tucked INSIDE the title.
        ///
        /// WHY A SECOND LABEL AT ALL: <c>StyleTextShadow</c> holds exactly one <c>TextShadow</c> — there
        /// is no comma-separated list as in CSS — so two shadows means two elements, full stop.
        ///
        /// WHY A CHILD RATHER THAN A SIBLING: a sibling would have to be positioned against the title's
        /// resolved rect and, worse, kept in step with the poster reveal, which animates the title's
        /// translate and opacity. A child inherits the parent's transform, its font, its size, its
        /// letter-spacing and its colour for free, so it tracks the animation exactly and there is no
        /// second set of styles to keep aligned.
        ///
        /// DRAW ORDER WORKS OUT: children paint after their parent, so the order on screen is the
        /// parent's deep shadow, then the parent's glyphs, then the echo's halo, then the echo's
        /// identical glyphs on top — the halo lands over the outer shadow where it belongs, and the
        /// glyphs the audience reads are clean.
        /// </summary>
        private static void SyncEcho(Label label, bool wanted)
        {
            var existing = label.Q<Label>(EchoName);

            if (!wanted)
            {
                existing?.RemoveFromHierarchy();
                return;
            }

            Label echo = existing;
            if (echo == null)
            {
                echo = new Label { name = EchoName };
                echo.AddToClassList(EchoClass);
                echo.AddToClassList(ShadowHaloClass);
                // Never interactive, and never a ray target in the world-space case.
                echo.pickingMode = PickingMode.Ignore;
                label.Add(echo);
            }

            // The title's text changes every slide, and an echo showing the previous slide's words
            // behind the current ones is the exact failure this has to not have.
            if (echo.text != label.text) echo.text = label.text;
        }
    }
}
