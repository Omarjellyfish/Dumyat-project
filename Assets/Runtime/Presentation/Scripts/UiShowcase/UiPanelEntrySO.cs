using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// One real UI panel, described once and used twice — as a tile in the overview grid, and as the
    /// subject of its own focus slide. That reuse is the reason this is an asset rather than fields on
    /// a slide: twelve panels would otherwise be described twenty-four times and drift apart.
    /// </summary>
    [CreateAssetMenu(fileName = "UiPanel", menuName = "Dumyat/Presentation/UI Panel Entry")]
    public sealed class UiPanelEntrySO : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField, Tooltip("Name shown under the tile and in the focus slide's lockup.")]
        private string _displayName;

        [SerializeField, TextArea(2, 4), Tooltip("One or two lines for the focus slide.")]
        private string _caption;

        [Header("The real panel")]
        [SerializeField, Tooltip("The SHIPPING UXML. Pointing at the real asset is the whole point — a " +
                                 "screenshot would go stale the first time somebody edits the panel.")]
        private VisualTreeAsset _uxml;

        [SerializeField, Tooltip("Stylesheets the panel needs. Most UXML files already include their " +
                                 "own <Style>; add here only what is applied by code in the real game.")]
        private StyleSheet[] _extraStyleSheets = new StyleSheet[0];

        [SerializeField, Tooltip("Render size in pixels. Match the panel's authored size — Delta menus " +
                                 "are 900x620, the workbench settings page is 1450x680.")]
        private Vector2Int _renderSize = new Vector2Int(900, 620);

        [Header("Content")]
        [SerializeField] private MockDataPopulatorSO _mockData;

        [Header("Presentation")]
        [SerializeField, Tooltip("Scale applied to the panel content before rendering. Below 1 shows " +
                                 "more of a large panel inside a fixed render texture.")]
        private float _contentScale = 1f;

        [SerializeField, Tooltip("Colour behind the panel in its render texture. Transparent lets the " +
                                 "slide's own backdrop show through the panel's rounded corners.")]
        private Color _clearColor = new Color(0f, 0f, 0f, 0f);

        public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
        public string Caption => _caption;
        public VisualTreeAsset Uxml => _uxml;
        public StyleSheet[] ExtraStyleSheets => _extraStyleSheets;
        public Vector2Int RenderSize => new Vector2Int(
            Mathf.Clamp(_renderSize.x, 64, 4096), Mathf.Clamp(_renderSize.y, 64, 4096));
        public MockDataPopulatorSO MockData => _mockData;
        public float ContentScale => Mathf.Clamp(_contentScale, 0.1f, 4f);
        public Color ClearColor => _clearColor;
    }
}
