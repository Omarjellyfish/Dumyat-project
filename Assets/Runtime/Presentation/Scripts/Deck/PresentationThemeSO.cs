using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The deck's palette. USS carries the layout and type scale; this carries the colours the
    /// renderers apply inline, so a second theme asset re-skins the whole deck without a second
    /// stylesheet.
    ///
    /// Defaults are the project's Delta values from CLAUDE.md, so a freshly created asset already
    /// looks like the rest of Dumyat.
    /// </summary>
    [CreateAssetMenu(fileName = "PresentationTheme", menuName = "Dumyat/Presentation/Theme")]
    public sealed class PresentationThemeSO : ScriptableObject
    {
        [Header("Delta palette")]
        [SerializeField, Tooltip("Primary text. Delta: #EAF8FB")]
        private Color _ink = new Color(0.918f, 0.973f, 0.984f, 1f);

        [SerializeField, Tooltip("Muted / label text. Delta: #D6F6FA at 55% alpha")]
        private Color _inkMuted = new Color(0.839f, 0.965f, 0.980f, 0.55f);

        [SerializeField, Tooltip("Accent. Delta cyan: #67DDE8")]
        private Color _accent = new Color(0.404f, 0.867f, 0.910f, 1f);

        [SerializeField, Tooltip("Positive / headline figures. Delta jade: #8DEBAF")]
        private Color _positive = new Color(0.553f, 0.922f, 0.686f, 1f);

        [SerializeField, Tooltip("Hairline dividers. Delta: #78E4EC at 42% alpha")]
        private Color _hairline = new Color(0.471f, 0.894f, 0.925f, 0.42f);

        [Header("Footer")]
        [SerializeField, Tooltip("Left-hand footer string, constant across the deck.")]
        private string _footerLeft = "Damietta";

        public Color Ink => _ink;
        public Color InkMuted => _inkMuted;
        public Color Accent => _accent;
        public Color Positive => _positive;
        public Color Hairline => _hairline;
        public string FooterLeft => _footerLeft;

        // NO ARABIC HELPER HERE — ON PURPOSE, AND IT USED TO BE THE OTHER WAY.
        //
        // The deck briefly ran its Arabic through ArabicTextShaper. That was the wrong tool: read its own
        // summary, which says it exists BECAUSE TMP cannot shape Arabic and that "UI Toolkit shapes
        // natively". Pre-shaping for UI Toolkit hands the Advanced generator text that is already in
        // presentation forms and has its Latin runs pre-reversed for a reversal that never comes, so it
        // shapes what is already shaped. That is what "looks bugged" was.
        //
        // The correct path is the project's existing one, and it is entirely declarative: put
        // UiLanguage.ArabicClass on the label, back it with a `.ui-arabic` rule that sets
        // `-unity-text-generator: advanced`, and give the panel an Arabic-capable PanelTextSettings. No
        // string is rewritten anywhere. See the "Arabic" block in PresentationStyles.uss.
    }
}
