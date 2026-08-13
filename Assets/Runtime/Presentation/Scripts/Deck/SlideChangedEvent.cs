namespace DumyatRuntime
{
    /// <summary>
    /// Published by <see cref="PresentationDirector"/> once a slide is on screen and the transition
    /// has finished. Anything that decorates the deck — the counter, the speaker notes, later the
    /// stage loader — reacts to this rather than holding a reference to the director.
    /// </summary>
    public readonly struct SlideChangedEvent : IGameEvent
    {
        /// <summary>Zero-based index of the slide now showing.</summary>
        public readonly int Index;

        /// <summary>Total slides in the deck, so subscribers can render "27 / 58" without asking back.</summary>
        public readonly int Count;

        /// <summary>The slide asset now showing. Never null when this event is published.</summary>
        public readonly SlideSO Slide;

        /// <summary>True when the move came from <see cref="IPresentationDirector.Previous"/>.</summary>
        public readonly bool WentBackwards;

        public SlideChangedEvent(int index, int count, SlideSO slide, bool wentBackwards)
        {
            Index = index;
            Count = count;
            Slide = slide;
            WentBackwards = wentBackwards;
        }
    }
}
