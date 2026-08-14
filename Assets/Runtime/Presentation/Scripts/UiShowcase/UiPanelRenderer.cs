using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Renders real UI panels into textures the deck can display.
    ///
    /// HOW, AND WHY THIS WAY:
    /// each panel gets its own <see cref="UIDocument"/> and its own runtime <see cref="PanelSettings"/>
    /// whose <c>targetTexture</c> is a RenderTexture. That is the only way to have several independent
    /// screen-space panels on screen at once — a UIDocument draws to one panel, and twelve panels in one
    /// tree would inherit each other's layout and stylesheets.
    ///
    /// Textures are built once on demand and cached for the life of the deck, because building twelve of
    /// them costs a visible hitch and the grid slide needs all twelve at the same moment. Everything is
    /// destroyed together in OnDestroy — runtime PanelSettings and RenderTextures are unmanaged and will
    /// leak quietly otherwise.
    /// </summary>
    public sealed class UiPanelRenderer : MonoBehaviour
    {
        [SerializeField, Tooltip("Theme used by every hosted panel. Leave empty to borrow the theme from " +
                                 "the deck's own PanelSettings.")]
        private ThemeStyleSheet _theme;

        [SerializeField, Tooltip("The deck's PanelSettings, used only to borrow a theme when none is set.")]
        private PanelSettings _themeSource;

        [SerializeField, Tooltip("Output resolution the deck is being presented at. Left empty, panels " +
                                 "render at their authored size — correct at 1080p, and visibly soft on " +
                                 "anything larger, because these are textures the slide then magnifies.")]
        private PresentationQualitySO _quality;

        private readonly Dictionary<UiPanelEntrySO, RenderTexture> _textures =
            new Dictionary<UiPanelEntrySO, RenderTexture>();
        private readonly List<Object> _owned = new List<Object>();
        private Transform _hostRoot;

        /// <summary>
        /// The panel drawn into a texture, built on first request. Returns null if the entry has no UXML,
        /// so callers can draw a placeholder tile rather than a broken one.
        /// </summary>
        public RenderTexture GetTexture(UiPanelEntrySO entry)
        {
            if (entry == null) return null;
            if (_textures.TryGetValue(entry, out RenderTexture cached) && cached != null) return cached;

            if (entry.Uxml == null)
            {
                Debug.LogWarning($"[UiPanelRenderer] '{entry.name}' has no UXML assigned.", entry);
                return null;
            }

            RenderTexture rt = Build(entry);
            _textures[entry] = rt;
            return rt;
        }

        private RenderTexture Build(UiPanelEntrySO entry)
        {
            // BOTH halves of this have to scale together. The texture is what the slide samples, and
            // PanelSettings below draws at ConstantPixelSize — so a bigger texture on its own would not
            // supersample the panel, it would simply give it more empty room around the same soft render.
            Vector2Int size = _quality != null
                ? _quality.PanelRenderSize(entry.RenderSize)
                : entry.RenderSize;

            var rt = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32)
            {
                name = "RT_" + entry.name,
                antiAliasing = 2,
            };
            rt.Create();
            _owned.Add(rt);

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "PS_" + entry.name;
            settings.themeStyleSheet = _theme != null ? _theme
                : (_themeSource != null ? _themeSource.themeStyleSheet : null);
            settings.targetTexture = rt;
            // ConstantPixelSize with a scale factor: the panel is authored at a fixed size, so scaling
            // with the "screen" would just fight the render texture's own dimensions.
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = _quality != null
                ? _quality.PanelContentScale(entry.ContentScale)
                : entry.ContentScale;
            settings.clearColor = true;
            settings.colorClearValue = entry.ClearColor;
            _owned.Add(settings);

            if (settings.themeStyleSheet == null)
                Debug.LogWarning($"[UiPanelRenderer] no theme for '{entry.name}' — controls will render " +
                                 "unstyled.", entry);

            if (_hostRoot == null)
            {
                // A SCENE ROOT, deliberately not a child of this component's GameObject.
                //
                // The deck's own UIDocument lives on the presentation rig. Unity treats a UIDocument
                // parented under another UIDocument as a CHILD document and asserts that it uses the
                // parent's PanelSettings — which is exactly what these must not do, since each panel
                // needs its own settings to target its own render texture. Parenting them under the rig
                // threw on the first panel and aborted the whole grid.
                var rootGo = new GameObject("__UI PANEL HOSTS");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rootGo, gameObject.scene);
                _hostRoot = rootGo.transform;
                _owned.Add(rootGo);
            }

            var hostGo = new GameObject("Host_" + entry.name);
            hostGo.transform.SetParent(_hostRoot, false);
            var doc = hostGo.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            doc.visualTreeAsset = entry.Uxml;

            VisualElement root = doc.rootVisualElement;
            if (root == null)
            {
                Debug.LogWarning($"[UiPanelRenderer] '{entry.name}' produced no root element.", entry);
                return rt;
            }

            foreach (StyleSheet sheet in entry.ExtraStyleSheets)
                if (sheet != null) root.styleSheets.Add(sheet);

            // Panels are authored to fill their parent; inside a render texture they need to be told to.
            root.style.width = new Length(100f, LengthUnit.Percent);
            root.style.height = new Length(100f, LengthUnit.Percent);

            if (entry.MockData != null) entry.MockData.Apply(root);
            else Debug.LogWarning($"[UiPanelRenderer] '{entry.name}' has no mock data — the panel will " +
                                  "render with whatever its UXML hard-codes.", entry);

            return rt;
        }

        /// <summary>Builds every panel up front, so the grid slide does not hitch on arrival.</summary>
        public void Prewarm(IEnumerable<UiPanelEntrySO> entries)
        {
            if (entries == null) return;
            int n = 0;
            foreach (UiPanelEntrySO e in entries) { if (GetTexture(e) != null) n++; }
            Debug.Log($"[UiPanelRenderer] prewarmed {n} panel texture(s).", this);
        }

        private void OnDestroy()
        {
            foreach (Object o in _owned)
            {
                if (o == null) continue;
                if (o is RenderTexture rt) rt.Release();
                Destroy(o);
            }
            _owned.Clear();
            _textures.Clear();
        }
    }
}
