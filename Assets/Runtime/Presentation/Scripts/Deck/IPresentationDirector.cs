namespace DumyatRuntime
{
    /// <summary>
    /// Navigation over the deck. Registered by <c>PresentationInstaller</c> so the input relay, the
    /// HUD and the slide renderers depend on this capability rather than on the concrete director.
    /// </summary>
    public interface IPresentationDirector
    {
        /// <summary>Zero-based index of the slide currently showing, or -1 before the first show.</summary>
        int Index { get; }

        /// <summary>Total slides in the deck.</summary>
        int Count { get; }

        /// <summary>The slide currently showing, or null before the first show.</summary>
        SlideSO Current { get; }

        /// <summary>
        /// True while a transition is mid-flight. Navigation is ignored during that window, which is
        /// what makes holding a key step one slide at a time instead of skipping three.
        /// </summary>
        bool IsTransitioning { get; }

        /// <summary>Advance one slide. No-op at the end of the deck.</summary>
        void Next();

        /// <summary>Go back one slide. No-op at the start of the deck.</summary>
        void Previous();

        /// <summary>
        /// Jump to the first slide of a section, 1-based to match the number keys and the printed
        /// contents page. Out-of-range numbers are ignored rather than clamped, so a mistyped key
        /// during a talk does nothing instead of jumping somewhere surprising.
        /// </summary>
        void JumpToSection(int sectionNumber);

        /// <summary>Return to the first slide.</summary>
        void Restart();
    }
}
