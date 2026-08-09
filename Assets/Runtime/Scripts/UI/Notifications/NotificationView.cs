using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DumyatRuntime
{
    /// <summary>
    /// Base for a single notification's visual style (see <see cref="NotificationToastView"/>,
    /// <see cref="NotificationMedalView"/>). The <see cref="NotificationHudManager"/> owns the world-space
    /// canvas, timing and animation; a view is a pure BUILDER: it (re)builds its card under the manager's
    /// canvas for one <see cref="NotificationData"/> and exposes the pieces the manager animates — the
    /// <see cref="Group"/> (fade), the <see cref="Content"/> (slide/scale) and the countdown fill
    /// (<see cref="SetProgress"/>). Swap the concrete view on the manager to change the look.
    /// </summary>
    public abstract class NotificationView : MonoBehaviour
    {
        [Header("Icon")]
        [Tooltip("Scales the rank icon within its badge/disc (1 = design default). Raise to make supplied " +
                 "sprites read bigger, lower to shrink them.")]
        [SerializeField, Range(0.3f, 2.5f)] private float _iconScale = 1f;

        protected RectTransform Parent;   // the manager's world-space canvas root
        protected RectTransform Card;     // the current card root (rebuilt per notification)
        protected CanvasGroup Group;      // fade handle for the manager
        protected Image ProgressFill;     // countdown bar fill (scaled on X)

        /// <summary>Designer-set icon size multiplier (1 = the view's design default), clamped &gt; 0.</summary>
        protected float IconScale => Mathf.Max(0.01f, _iconScale);

        /// <summary>Fade/animation handle for the current card (null before the first Build).</summary>
        public CanvasGroup GroupHandle => Group;

        /// <summary>The current card's transform, for the manager's slide/scale-in.</summary>
        public RectTransform ContentHandle => Card;

        /// <summary>Called once by the manager with the canvas root everything is built under.</summary>
        public void Attach(RectTransform parent) => Parent = parent;

        /// <summary>(Re)builds the card for <paramref name="data"/>. Sets <see cref="Group"/>/<see cref="ProgressFill"/>.</summary>
        public abstract void Build(NotificationData data);

        /// <summary>Sets the countdown fill (1 = full, 0 = empty), left-anchored.</summary>
        public void SetProgress(float t01)
        {
            if (ProgressFill != null)
                ProgressFill.rectTransform.localScale = new Vector3(Mathf.Clamp01(t01), 1f, 1f);
        }

        /// <summary>Hides the card between notifications without destroying the view component.</summary>
        public void HideCard()
        {
            if (Card != null) Card.gameObject.SetActive(false);
        }

        // Tears down the previous card so Build always starts clean.
        protected void ClearCard()
        {
            if (Card != null) Destroy(Card.gameObject);
            Card = null;
            Group = null;
            ProgressFill = null;
        }
    }

    /// <summary>
    /// Procedural UI toolkit for the notification views: cached rounded-rect / circle sprites (so world-space
    /// panels get crisp rounded corners with no imported art) plus terse builders for images and text. Colours
    /// match the approved design mockup (dark glass over the bright market, per-tier accent).
    /// </summary>
    internal static class NotifUI
    {
        public static readonly Color Glass  = Hex("#141E24");   // card ground (dark, translucent-looking)
        public static readonly Color Ground = Hex("#0B1114");   // deep inner (medal disc, badges)
        public static readonly Color Text   = Hex("#F4EEE2");
        public static readonly Color Muted  = Hex("#A6ADA7");
        public static readonly Color BarBg  = new Color(1f, 1f, 1f, 0.09f);

        private static readonly Dictionary<int, Sprite> _rounded = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> _circle = new Dictionary<int, Sprite>();

        public static Color Hex(string hex) =>
            ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;

        public static Color Mix(Color a, Color b, float t) => Color.Lerp(a, b, t);

        // 9-sliced rounded rectangle. One texture per corner radius, reused (and tinted per Image).
        public static Sprite Rounded(int radius)
        {
            radius = Mathf.Max(1, radius);
            if (_rounded.TryGetValue(radius, out Sprite s)) return s;

            int size = radius * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius), 0f);
                    float dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - d + 0.5f);   // 1px anti-aliased edge
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();

            float b = radius + 1;
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            _rounded[radius] = s;
            return s;
        }

        // Solid anti-aliased circle at a native diameter (not sliced — used at fixed sizes).
        public static Sprite Circle(int diameter)
        {
            diameter = Mathf.Max(4, diameter);
            if (_circle.TryGetValue(diameter, out Sprite s)) return s;

            var tex = new Texture2D(diameter, diameter, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float r = diameter / 2f;
            var px = new Color32[diameter * diameter];
            for (int y = 0; y < diameter; y++)
                for (int x = 0; x < diameter; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    px[y * diameter + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();

            s = Sprite.Create(tex, new Rect(0, 0, diameter, diameter), new Vector2(0.5f, 0.5f), 100f);
            _circle[diameter] = s;
            return s;
        }

        // --- element builders ---------------------------------------------------

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static Image Image(Transform parent, string name, Sprite sprite, Color color, bool sliced)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = sliced ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, string text, float size,
            Color color, TextAlignmentOptions align, bool bold, bool outline = false,
            float autoSizeMin = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();

            // Arabic needs BOTH halves — see ArabicTextShaper. TMP has no shaper, so an unshaped
            // string renders as disconnected isolated letters running the wrong way. Notifications
            // carry quest names and objectives straight from the SO, so they hit this every time the
            // player is in Arabic.
            bool arabic = ArabicTextShaper.ContainsArabic(text);
            t.text = ArabicTextShaper.Shape(text);
            t.isRightToLeftText = arabic;

            t.fontSize = size;
            t.color = color;

            // A left-aligned card reads as ragged-right in Arabic unless the whole block flips.
            t.alignment = arabic ? Rtl(align) : align;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;

            // Quest objectives are authored prose of no fixed length, so the card cannot assume a
            // line count. Shrinking to a floor keeps a long line inside the card instead of spilling
            // past it, and the floor is what stops it shrinking below what a headset can resolve.
            if (autoSizeMin > 0f)
            {
                t.enableAutoSizing = true;
                t.fontSizeMin = autoSizeMin;
                t.fontSizeMax = size;
                t.overflowMode = TextOverflowModes.Ellipsis;   // last resort, after auto-size bottoms out
            }
            t.raycastTarget = false;
            t.richText = true;
            if (outline)
            {
                // Per-object material instance so the shared font material is untouched (as SubtitlePanel does).
                Material m = t.fontMaterial;
                m.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.18f);
                m.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 0.9f));
                t.UpdateMeshPadding();
            }
            return t;
        }

        /// <summary>
        /// Mirrors a horizontal alignment for right-to-left text. Only the alignments the cards
        /// actually use are mapped; anything centred is already correct and passes through.
        /// </summary>
        private static TextAlignmentOptions Rtl(TextAlignmentOptions align) => align switch
        {
            TextAlignmentOptions.TopLeft => TextAlignmentOptions.TopRight,
            TextAlignmentOptions.Left => TextAlignmentOptions.Right,
            TextAlignmentOptions.BottomLeft => TextAlignmentOptions.BottomRight,
            _ => align
        };

        // Stretches a RectTransform to fill its parent with optional per-side inset.
        public static void Fill(RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }

        public static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
    }
}
