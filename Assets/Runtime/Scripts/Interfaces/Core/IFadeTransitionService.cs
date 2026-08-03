namespace DumyatRuntime
{
    /// <summary>
    /// Handles screen fade transitions during scene loading.
    /// Fades out, persists the player across the load, then fades back in.
    /// </summary>
    public interface IFadeTransitionService
    {
        /// <summary>
        /// Whether a transition is currently in progress.
        /// </summary>
        bool IsTransitioning { get; }

        /// <summary>
        /// Starts a fade-out → scene load → fade-in transition.
        /// </summary>
        void StartTransition(string destinationScene);
    }
}
