using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// The one place UI Toolkit panels ask "which language am I drawing in, and what does that mean
    /// for the text?".
    ///
    /// Panels were each growing their own copy of this after the tutorial card gained Arabic — the
    /// language lookup, the fallback rule, and the shaping class are identical everywhere, so they
    /// live here rather than being re-derived per panel.
    ///
    /// This is the UI Toolkit path only. <see cref="ArabicTextShaper"/> exists because TMP cannot
    /// shape Arabic; UI Toolkit's Advanced text generator does it natively, so nothing here rewrites
    /// the string — it only switches the generator on.
    /// </summary>
    public static class UiLanguage
    {
        /// <summary>
        /// Added to every text element while drawing Arabic. The rule itself lives in each panel's
        /// USS (see the "Arabic" block) because a class with no stylesheet behind it does nothing.
        /// </summary>
        public const string ArabicClass = "ui-arabic";

        /// <summary>
        /// Whether panels should draw in Arabic.
        ///
        /// <see cref="SubtitleMode.Off"/> deliberately reads as English rather than blanking or
        /// hiding anything: "off" means the player does not want merchant captions, not that the
        /// interface should stop having words in it. <see cref="ISettingsService.SubtitleLanguage"/>
        /// already collapses Off to English, so that decision is made in exactly one place.
        ///
        /// A missing service (a scene with no ContainerScope) degrades to English for the same
        /// reason a missing dependency should never look like a broken panel.
        /// </summary>
        public static bool IsArabic(ISettingsService settings) =>
            (settings?.SubtitleLanguage ?? GameLanguage.English) == GameLanguage.Arabic;

        /// <summary>
        /// Picks the Arabic string, falling back to English when none was authored. Content assets
        /// gain their Arabic fields over time, so a half-translated database still reads rather than
        /// showing gaps.
        /// </summary>
        public static string Pick(bool arabic, string english, string arabicText) =>
            arabic && !string.IsNullOrWhiteSpace(arabicText) ? arabicText : english;

        /// <summary>
        /// Switches the Arabic text generator on (or off) for every text element under
        /// <paramref name="root"/>.
        ///
        /// Applied by walking the tree rather than by tagging elements as they are built, because
        /// these panels create most of their content in C# — rows, chips, cards — and one call after
        /// a rebuild is far harder to forget than a class on every `new Label`.
        ///
        /// Call it AFTER repopulating a panel: elements added later start without the class.
        /// </summary>
        public static void ApplyScript(VisualElement root, bool arabic)
        {
            root?.Query<TextElement>().ForEach(e => e.EnableInClassList(ArabicClass, arabic));
        }
    }
}
