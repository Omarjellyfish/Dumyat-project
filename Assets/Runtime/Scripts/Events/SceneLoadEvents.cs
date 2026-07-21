namespace DumyatRuntime
{
    /// <summary>
    /// Fired when a new scene begins loading. Use this to fade to black, stop music, and cancel haptics.
    /// </summary>
    public struct SceneLoadBeginEvent : IGameEvent
    {
        public string TargetSceneName;
    }

    /// <summary>
    /// Fired periodically during the background load to update loading bars.
    /// </summary>
    public struct SceneLoadProgressEvent : IGameEvent
    {
        public float Progress;
    }

    /// <summary>
    /// Fired when the target scene has fully loaded and initialized. Use this to fade back in.
    /// </summary>
    public struct SceneLoadCompleteEvent : IGameEvent
    {
        public string LoadedSceneName;
    }
}
