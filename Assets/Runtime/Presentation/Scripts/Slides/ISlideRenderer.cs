namespace DumyatRuntime
{
    /// <summary>
    /// Presents one kind of slide. One implementation per <see cref="SlideSO"/> subclass, resolved by
    /// <see cref="SlideRendererRegistry"/> — which is what makes a new slide type an additive change
    /// rather than an edit to the director.
    /// </summary>
    public interface ISlideRenderer
    {
        /// <summary>True if this renderer handles the given slide asset.</summary>
        bool CanRender(SlideSO slide);

        /// <summary>
        /// Put the slide on screen. Always called while the transition has the screen covered, so
        /// this may rebuild elements freely without anyone seeing the intermediate state.
        /// </summary>
        void Show(SlideSO slide, PresentationDeckSO deck);

        /// <summary>Take this renderer's content off screen.</summary>
        void Hide();
    }
}
