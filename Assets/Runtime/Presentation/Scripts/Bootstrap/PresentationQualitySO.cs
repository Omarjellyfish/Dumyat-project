using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>Output resolutions the deck can be presented at.</summary>
    public enum PresentationResolution
    {
        /// <summary>1920 x 1080. The resolution every stylesheet in the deck is authored against.</summary>
        FullHD = 0,

        /// <summary>2560 x 1440 — "2K" as everyone actually uses the word (QHD), not the 2048 DCI one.</summary>
        TwoK = 1,

        /// <summary>3840 x 2160.</summary>
        FourK = 2,
    }

    /// <summary>
    /// What the deck is being presented ON, and everything that has to follow from it.
    ///
    /// WHY ONE ASSET RATHER THAN A NUMBER IN THREE PLACES:
    /// raising the output resolution is not one change. The UI scales for free — PanelSettings is
    /// ScaleWithScreenSize against a 1920x1080 reference, so every px in the stylesheets, the grid tile
    /// maths and the crop-window maths are REFERENCE pixels and land correctly at any output size. But
    /// two things are baked in real pixels and quietly get WORSE as the screen gets bigger:
    ///
    ///   1. the UI showcase panels, each rendered to a fixed-size RenderTexture and then drawn scaled;
    ///   2. the video render textures, capped at a fixed width.
    ///
    /// Both are magnified rather than re-rendered, so at 4K they are the only soft things on an
    /// otherwise sharper screen — most visibly on the slides whose entire job is showing UI craft.
    /// Deriving them from one setting is what stops that going unnoticed.
    ///
    /// Nothing here scales layout. Layout is already resolution-independent; this is only about the
    /// resolution things are RASTERISED at.
    /// </summary>
    [CreateAssetMenu(fileName = "PresentationQuality", menuName = "Dumyat/Presentation/Quality")]
    public sealed class PresentationQualitySO : ScriptableObject
    {
        /// <summary>The width every stylesheet and every hard-coded layout number is authored against.</summary>
        public const float ReferenceWidth = 1920f;

        [Header("Output")]
        [SerializeField, Tooltip("What the deck is being shown on.\n\nFull HD 1920x1080 · 2K 2560x1440 · " +
                                 "4K 3840x2160.\n\nLayout does not change — only how sharply the panels " +
                                 "and footage are rendered.")]
        private PresentationResolution _resolution = PresentationResolution.TwoK;

        [SerializeField, Tooltip("Set the player's resolution to match at boot.\n\nIgnored in the Editor, " +
                                 "where it would only fight the Game view's own aspect and size controls.")]
        private bool _applyScreenResolution = true;

        [SerializeField, Tooltip("Full screen when applying the resolution. Off gives a window, which is " +
                                 "handy for rehearsing next to notes.")]
        private bool _fullScreen = true;

        [Header("Pipeline")]
        [SerializeField, Tooltip("Quality level to select at boot, by name. Empty selects none.\n\n" +
                                 "\"PC\" is already the Standalone default, so a BUILT deck gets " +
                                 "PC_RPAsset — 2048 shadowmap, four cascades, soft shadows. The Editor " +
                                 "sits on \"Mobile\" though, so previewing looks worse than shipping. " +
                                 "Naming it here makes the two agree.\n\nNOTE: this changes the Editor's " +
                                 "current quality level as a side effect, the same as picking it from " +
                                 "the Quality settings dropdown.")]
        private string _qualityLevelName = "PC";

        [Header("Panel rasterisation")]
        [SerializeField, Range(0.5f, 2f),
         Tooltip("Extra multiplier on top of the resolution's own scaling, for the UI showcase panels.\n\n" +
                 "1 renders each panel at exactly the pixels it occupies. Above 1 supersamples it — " +
                 "worth it if the panels still read soft, at four times the memory for double the number.")]
        private float _panelRenderScale = 1f;

        [SerializeField, Range(512, 8192),
         Tooltip("Hard ceiling on either dimension of a panel's render texture, whatever the maths asks " +
                 "for. Stops one oversized panel asset allocating something absurd.")]
        private int _maxPanelDimension = 4096;

        public PresentationResolution Resolution => _resolution;

        public Vector2Int TargetSize
        {
            get
            {
                switch (_resolution)
                {
                    case PresentationResolution.FullHD: return new Vector2Int(1920, 1080);
                    case PresentationResolution.FourK: return new Vector2Int(3840, 2160);
                    default: return new Vector2Int(2560, 1440);
                }
            }
        }

        /// <summary>
        /// How much bigger the output is than the authoring reference: 1, 1.33 or 2.
        /// This is the factor anything rasterised at a fixed size has to be multiplied by to keep pace.
        /// </summary>
        public float OutputScale => TargetSize.x / ReferenceWidth;

        /// <summary>Widest render texture a single video clip may be given. Never exceeds the source.</summary>
        public int MaxVideoWidth => TargetSize.x;

        /// <summary>
        /// The render-texture size for a panel authored at <paramref name="authored"/>.
        ///
        /// The caller must apply <see cref="PanelContentScale"/> to the PanelSettings as well. The
        /// showcase renders panels at ConstantPixelSize, so a bigger texture ALONE just gives the panel
        /// more empty room around it — the content has to be told to draw bigger too, or the slide shows
        /// the same soft panel floating in a larger frame.
        /// </summary>
        public Vector2Int PanelRenderSize(Vector2Int authored)
        {
            float scale = PanelScale;
            return new Vector2Int(
                Mathf.Clamp(Mathf.RoundToInt(authored.x * scale), 64, _maxPanelDimension),
                Mathf.Clamp(Mathf.RoundToInt(authored.y * scale), 64, _maxPanelDimension));
        }

        /// <summary>Total factor applied to a panel's texture: output scale times the manual dial.</summary>
        public float PanelScale => OutputScale * Mathf.Clamp(_panelRenderScale, 0.5f, 2f);

        /// <summary>
        /// What a panel's own PanelSettings.scale must become. Multiply the entry's authored content
        /// scale by this.
        /// </summary>
        public float PanelContentScale(float authoredContentScale) => authoredContentScale * PanelScale;

        /// <summary>
        /// Selects the quality level and, in a player, the screen resolution. Called once at boot.
        /// </summary>
        public void Apply()
        {
            SelectQualityLevel();

            if (!_applyScreenResolution) return;

            // The Game view owns its own size and aspect; forcing a resolution there fights the user
            // rather than telling us anything about how the deck will look on the night.
            if (Application.isEditor)
            {
                Debug.Log($"[PresentationQuality] {_resolution} ({TargetSize.x}x{TargetSize.y}) — screen " +
                          "resolution not applied in the Editor. Set the Game view aspect to 16:9 to " +
                          "judge framing.", this);
                return;
            }

            Vector2Int size = TargetSize;
            Screen.SetResolution(size.x, size.y,
                _fullScreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);

            Debug.Log($"[PresentationQuality] {_resolution} — screen set to {size.x}x{size.y}" +
                      (_fullScreen ? " full screen." : " windowed."), this);
        }

        private void SelectQualityLevel()
        {
            if (string.IsNullOrWhiteSpace(_qualityLevelName)) return;

            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] != _qualityLevelName) continue;
                if (QualitySettings.GetQualityLevel() == i) return;

                // applyExpensiveChanges: this runs once at boot, before anyone is watching, and the
                // whole point is the shadow and cascade settings that flag is what actually applies.
                QualitySettings.SetQualityLevel(i, true);
                Debug.Log($"[PresentationQuality] quality level → '{names[i]}'.", this);
                return;
            }

            Debug.LogWarning($"[PresentationQuality] no quality level called '{_qualityLevelName}'. " +
                             $"Known: {string.Join(", ", names)}.", this);
        }
    }
}
