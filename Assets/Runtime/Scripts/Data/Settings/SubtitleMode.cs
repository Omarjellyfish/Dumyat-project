namespace DumyatRuntime
{
    /// <summary>
    /// What the merchant subtitle panel shows. Deliberately one enum rather than a separate
    /// on/off bool plus a <see cref="GameLanguage"/>: the player picks a single thing in the
    /// settings panel, and two fields that can disagree ("off, but Arabic") is a bug waiting to
    /// happen. <see cref="ISettingsService"/> derives the older bool/language pair from this.
    /// </summary>
    public enum SubtitleMode
    {
        Off = 0,
        English = 1,
        Arabic = 2
    }
}
