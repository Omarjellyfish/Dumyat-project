namespace DumyatRuntime
{
    /// <summary>
    /// The mixer groups a player can set a level for. One entry per exposed parameter on
    /// <c>Assets/Runtime/MainMixer.mixer</c>; see <see cref="SettingsKeys"/> for the parameter
    /// names, which are NOT simply these values stringified.
    /// </summary>
    public enum AudioChannel
    {
        Master = 0,
        Music = 1,
        SoundFX = 2,
        Ambience = 3,
        Dialogue = 4
    }
}
