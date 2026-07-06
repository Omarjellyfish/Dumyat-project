namespace DumyatRuntime
{
    public interface ISceneLoadService
    {
        /// <summary>
        /// Current loading progress of the target scene (0 to 1).
        /// </summary>
        float CurrentProgress { get; }

        /// <summary>
        /// True if a load is currently happening.
        /// </summary>
        bool IsLoading { get; }

        /// <summary>
        /// Starts the VR-friendly loading sequence.
        /// </summary>
        /// <param name="targetSceneName">The scene you want to play.</param>
        /// <param name="loadingSceneName">The VR environment to sit in while loading (e.g. "Car"). If empty, uses standard load.</param>
        void LoadScene(string targetSceneName, string loadingSceneName = "");

        /// <summary>
        /// If the loading is paused waiting for user input, call this to finish the transition.
        /// </summary>
        void AllowSceneActivation(string loadedSceneName = "");
    }
}
