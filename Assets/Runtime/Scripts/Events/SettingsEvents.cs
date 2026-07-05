namespace DumyatRuntime
{
    /// <summary>Published by <see cref="SettingsManager"/> when subtitle settings change.</summary>
    public readonly struct SubtitleSettingsChangedEvent : IGameEvent
    {
        public readonly bool Enabled;
        public readonly int Size;

        public SubtitleSettingsChangedEvent(bool enabled, int size)
        {
            Enabled = enabled;
            Size = size;
        }
    }
}
