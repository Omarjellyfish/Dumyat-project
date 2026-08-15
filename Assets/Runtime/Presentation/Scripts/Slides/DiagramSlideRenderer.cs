using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Draws a <see cref="DiagramSlideSO"/>: connectors first, then the marks on top.
    ///
    /// Everything is positioned in the deck's 1920x1080 reference space, the same space the stylesheet is
    /// authored in, so a diagram composed once is correct at every output resolution the quality asset
    /// offers. Node positions are stored normalised precisely so that this conversion is the only place
    /// resolution enters the picture.
    ///
    /// A connector is a thin box rotated to face its target — UI Toolkit has no line primitive, and a
    /// rotated box is the honest equivalent. Rotation happens about the element's centre, which is why
    /// each one is positioned by its midpoint rather than its start.
    /// </summary>
    public sealed class DiagramSlideRenderer : MonoBehaviour, ISlideRenderer
    {
        private const float RefWidth = 1920f;
        private const float RefHeight = 1080f;

        [SerializeField] private PresentationView _view;

        private VisualElement _host;

        private void Awake()
        {
            if (_view == null) _view = GetComponentInParent<PresentationView>();
        }

        private bool EnsureHost()
        {
            if (_host != null) return true;
            if (_view == null || !_view.IsReady) return false;

            _host = new VisualElement { name = "diagram-host" };
            _host.AddToClassList("diagram-host");
            _host.pickingMode = PickingMode.Ignore;
            _view.Content.Add(_host);
            _host.AddToClassList("hidden");
            return true;
        }

        public bool CanRender(SlideSO slide) => slide is DiagramSlideSO;

        public void Hide()
        {
            if (_host != null) _host.AddToClassList("hidden");
            if (_view != null && _view.IsReady)
            {
                _view.Scrim.AddToClassList("hidden");
                _view.Lockup.AddToClassList("hidden");
            }
        }

        public void Show(SlideSO slide, PresentationDeckSO deck)
        {
            if (!EnsureHost()) return;
            if (!(slide is DiagramSlideSO diagram))
            {
                Debug.LogWarning($"[DiagramSlideRenderer] asked to render {slide?.GetType().Name}.", this);
                return;
            }

            _view.Scrim.AddToClassList("hidden");
            _view.Lockup.AddToClassList("hidden");

            _host.RemoveFromClassList("hidden");
            _host.Clear();

            PresentationThemeSO theme = deck != null ? deck.Theme : null;

            if (diagram.ShowTitle && !string.IsNullOrEmpty(diagram.Title))
            {
                var title = new Label(diagram.Title);
                title.AddToClassList("diagram-title");
                if (theme != null) title.style.color = theme.Ink;
                _host.Add(title);
            }

            DiagramNode[] nodes = diagram.Nodes;

            // Connectors underneath, so a line never crosses the symbol it points at.
            foreach (DiagramEdge edge in diagram.Edges)
            {
                if (edge.From < 0 || edge.From >= nodes.Length) continue;
                if (edge.To < 0 || edge.To >= nodes.Length) continue;
                DrawEdge(nodes[edge.From], nodes[edge.To], edge, theme);
            }

            foreach (DiagramNode node in nodes) DrawNode(node, theme);
        }

        // ---- marks -----------------------------------------------------------------------------

        private void DrawNode(DiagramNode node, PresentationThemeSO theme)
        {
            Vector2 p = ToReference(node.Position);
            Color colour = Resolve(node.Tint, theme);
            Color accent = Resolve(DiagramTint.Accent, theme);

            if (node.Kind == DiagramNodeKind.Pill)
            {
                DrawPill(node, p, colour, accent, theme);
                return;
            }

            float size = node.Size > 0f ? node.Size : 120f;
            VisualElement icon = DiagramIcons.Build(node.Icon, size, colour, accent);
            icon.style.left = p.x - size * 0.5f;
            icon.style.top = p.y - size * 0.5f;
            _host.Add(icon);
        }

        /// <summary>
        /// A plate carrying a short string and its priority pips — the one place a diagram slide holds
        /// words, for when the content genuinely is a name and no symbol can stand in for it.
        /// </summary>
        private void DrawPill(DiagramNode node, Vector2 p, Color colour, Color accent,
                              PresentationThemeSO theme)
        {
            float width = node.Size > 0f ? node.Size : 470f;
            float height = PillHeight(node);

            var plate = new VisualElement { pickingMode = PickingMode.Ignore };
            plate.AddToClassList("diagram-pill");
            if (node.Filled) plate.AddToClassList("diagram-pill--filled");
            plate.style.left = p.x - width * 0.5f;
            plate.style.top = p.y - height * 0.5f;
            plate.style.width = width;
            plate.style.height = height;

            Color edge = node.Filled ? accent : Resolve(DiagramTint.Muted, theme);
            plate.style.borderTopColor = edge;
            plate.style.borderBottomColor = edge;
            plate.style.borderLeftColor = edge;
            plate.style.borderRightColor = edge;
            if (node.Filled)
            {
                Color fill = accent;
                fill.a = 0.16f;
                plate.style.backgroundColor = fill;
            }

            if (!string.IsNullOrEmpty(node.Label))
            {
                var label = new Label(node.Label);
                label.AddToClassList("diagram-pill-label");
                label.style.color = node.Filled
                    ? (theme != null ? theme.Ink : Color.white)
                    : Resolve(DiagramTint.Muted, theme);
                plate.Add(label);
            }

            _host.Add(plate);

            // Pips sit outside the plate, so a longer name never squeezes them.
            for (int i = 0; i < node.Pips; i++)
            {
                VisualElement pip = DiagramIcons.Rect(
                    p.x + width * 0.5f + 42f + i * 24f, p.y, 14f, 14f,
                    node.Filled ? accent : Resolve(DiagramTint.Muted, theme), 7f);
                _host.Add(pip);
            }
        }

        // ---- connectors ------------------------------------------------------------------------

        private void DrawEdge(DiagramNode from, DiagramNode to, DiagramEdge edge, PresentationThemeSO theme)
        {
            Vector2 a = ToReference(from.Position);
            Vector2 b = ToReference(to.Position);

            Vector2 dir = (b - a).normalized;

            // Stop short of both marks so the line meets them rather than running underneath.
            //
            // The inset has to be measured ALONG THE EDGE, not from a single radius: a pill's Size is
            // its WIDTH, so treating that as a radius made a vertical connector between two stacked
            // pills inset by half their width over a gap of a fraction of that — the line overshot its
            // own target and drew backwards. Projecting each node's half-extents onto the direction
            // handles wide-and-short marks and square ones with the same arithmetic.
            a += dir * Extent(from, dir) * 0.92f;
            b -= dir * Extent(to, dir) * 0.92f;

            if (Vector2.Dot(b - a, dir) <= 1f) return;   // the marks touch; there is no line to draw

            Color colour = Resolve(edge.Tint, theme);
            float length = Vector2.Distance(a, b);
            if (length < 1f) return;

            float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            Vector2 mid = (a + b) * 0.5f;

            if (edge.Dashed)
            {
                // No dashed borders in USS, so a dashed line is a row of short boxes laid along it.
                const float Dash = 18f, Gap = 22f;
                int count = Mathf.Max(1, Mathf.FloorToInt(length / (Dash + Gap)));
                for (int i = 0; i < count; i++)
                {
                    float t = (i + 0.5f) / count;
                    Vector2 c = Vector2.Lerp(a, b, t);
                    VisualElement seg = DiagramIcons.Rect(c.x, c.y, Dash, 3f, colour, 1.5f);
                    seg.style.rotate = new Rotate(angle);
                    _host.Add(seg);
                }
            }
            else
            {
                VisualElement line = DiagramIcons.Rect(mid.x, mid.y, length, 3f, colour, 1.5f);
                line.style.rotate = new Rotate(angle);
                _host.Add(line);
            }

            if (edge.Arrow) DrawArrowhead(b, angle, colour);

            // A route that deliberately does not exist. The strike is the statement, so it is drawn
            // last and in whatever tint the edge asked for rather than the line's own.
            if (edge.Struck)
            {
                VisualElement bar = DiagramIcons.Rect(mid.x, mid.y, 96f, 5f, colour, 2.5f);
                bar.style.rotate = new Rotate(angle + 90f);
                _host.Add(bar);

                DiagramIcons.Ring(_host, mid.x, mid.y, 104f, 5f, colour);
            }
        }

        /// <summary>Two short bars meeting at a point — a chevron, since there is no polygon.</summary>
        private void DrawArrowhead(Vector2 tip, float angle, Color colour)
        {
            const float Arm = 26f;

            VisualElement upper = DiagramIcons.Rect(tip.x, tip.y, Arm, 4f, colour, 2f);
            upper.style.rotate = new Rotate(angle + 148f);
            upper.style.transformOrigin = new TransformOrigin(new Length(0f, LengthUnit.Percent),
                                                              new Length(50f, LengthUnit.Percent));
            _host.Add(upper);

            VisualElement lower = DiagramIcons.Rect(tip.x, tip.y, Arm, 4f, colour, 2f);
            lower.style.rotate = new Rotate(angle - 148f);
            lower.style.transformOrigin = new TransformOrigin(new Length(0f, LengthUnit.Percent),
                                                              new Length(50f, LengthUnit.Percent));
            _host.Add(lower);
        }

        // ---- shared ----------------------------------------------------------------------------

        /// <summary>
        /// Half the mark's size measured along <paramref name="dir"/>, treating it as a box. An icon is
        /// square so both axes agree; a pill is wide and short, so a sideways edge insets by half its
        /// width and a stacked one by half its height.
        /// </summary>
        private static float Extent(DiagramNode node, Vector2 dir)
        {
            float halfW = (node.Size > 0f ? node.Size : 120f) * 0.5f;
            float halfH = node.Kind == DiagramNodeKind.Pill
                ? PillHeight(node) * 0.5f
                : halfW;

            return Mathf.Abs(dir.x) * halfW + Mathf.Abs(dir.y) * halfH;
        }

        /// <summary>A filled plate reads as the active choice, so it is drawn a little taller.</summary>
        private static float PillHeight(DiagramNode node) => node.Filled ? 80f : 66f;

        private static Vector2 ToReference(Vector2 normalised) =>
            new Vector2(normalised.x * RefWidth, normalised.y * RefHeight);

        private static Color Resolve(DiagramTint tint, PresentationThemeSO theme)
        {
            switch (tint)
            {
                case DiagramTint.Accent: return theme != null ? theme.Accent : new Color(0.404f, 0.867f, 0.91f);
                case DiagramTint.Jade: return theme != null ? theme.Positive : new Color(0.553f, 0.922f, 0.686f);
                case DiagramTint.Coral: return new Color(1f, 0.608f, 0.541f);
                case DiagramTint.Muted: return theme != null ? theme.InkMuted : new Color(0.84f, 0.965f, 0.98f, 0.55f);
                default: return theme != null ? theme.Ink : new Color(0.918f, 0.973f, 0.984f);
            }
        }
    }
}
