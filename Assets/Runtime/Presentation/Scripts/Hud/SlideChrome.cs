using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Shows or hides the deck's shared chrome — the footer row and the progress rail.
    ///
    /// WHY THIS IS SHARED: more than one renderer clears the frame. A theatre video and a bare poster
    /// both want nothing on screen but the image, and both hand these same elements on to whatever
    /// slide follows. One copy means one answer to "what counts as chrome", so the next element added
    /// to the bottom edge is hidden by every slide that already asked for an empty frame instead of by
    /// whichever renderer someone remembered to edit.
    ///
    /// The speaker-notes panel is deliberately NOT chrome: it is a presenter aid toggled with N, never
    /// part of the composition, and no slide should be able to take the presenter's notes away.
    ///
    /// Callers must restore it at the top of Show() as well as in Hide(). The registry only calls
    /// Hide() when the renderer actually CHANGES, so two text slides in a row would otherwise leave
    /// the second one wearing the first one's empty frame.
    /// </summary>
    public static class SlideChrome
    {
        public static void SetVisible(PresentationView view, bool visible)
        {
            if (view == null || !view.IsReady) return;

            view.Footer?.EnableInClassList("hidden", !visible);
            view.ProgressTrack?.EnableInClassList("hidden", !visible);
        }
    }
}
