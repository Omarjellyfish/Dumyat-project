using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Presents a <see cref="UiShowcaseSlideSO"/> — a grid of every panel, or one panel large.
    ///
    /// The tiles and the focus frame are built here rather than authored in the deck's UXML because both
    /// are entirely data-shaped: twelve panels is a number that will change, and a hand-authored grid
    /// would have to be re-laid-out every time it does.
    /// </summary>
    public sealed class UiShowcaseRenderer : MonoBehaviour, ISlideRenderer
    {
        [SerializeField] private PresentationView _view;
        [SerializeField] private UiPanelRenderer _panels;

        private VisualElement _host;

        private void Awake()
        {
            if (_view == null) _view = GetComponentInParent<PresentationView>();
            if (_panels == null) _panels = GetComponentInParent<UiPanelRenderer>();
            if (_panels == null) _panels = FindFirstObjectByType<UiPanelRenderer>();
        }

        /// <summary>
        /// Builds the host container on first use, NOT in Awake — Awake order between components on one
        /// GameObject is not dependable, and building it there silently did nothing whenever
        /// PresentationView had not resolved its tree yet.
        /// </summary>
        private bool EnsureHost()
        {
            if (_host != null) return true;
            if (_view == null || !_view.IsReady) return false;

            // Own container inside the content layer, so hiding this renderer cannot disturb the text
            // slide's tree and vice versa.
            _host = new VisualElement { name = "ui-showcase-host" };
            _host.AddToClassList("ui-showcase-host");
            _host.pickingMode = PickingMode.Ignore;
            _view.Content.Add(_host);
            _host.AddToClassList("hidden");
            return true;
        }

        public bool CanRender(SlideSO slide) => slide is UiShowcaseSlideSO;

        public void Hide()
        {
            if (_host != null) _host.AddToClassList("hidden");
        }

        public void Show(SlideSO slide, PresentationDeckSO deck)
        {
            if (!EnsureHost()) return;
            if (!(slide is UiShowcaseSlideSO ui))
            {
                Debug.LogWarning($"[UiShowcaseRenderer] asked to render {slide?.GetType().Name}.", this);
                return;
            }

            _host.RemoveFromClassList("hidden");
            _host.Clear();
            // Cleared with the host: these point at elements that no longer exist, and a stale entry
            // would have the scheduled callback animating a detached element.
            _zoomTargets.Clear();

            // Eyebrow and title come from the shared chrome; the panels are the content.
            PresentationThemeSO theme = deck != null ? deck.Theme : null;
            DrawHeader(ui, deck, theme);

            if (ui.Mode == UiShowcaseMode.Grid) DrawGrid(ui, theme);
            else DrawFocus(ui, theme);
        }

        private void DrawHeader(UiShowcaseSlideSO ui, PresentationDeckSO deck, PresentationThemeSO theme)
        {
            string sectionName = deck != null ? deck.ResolveSectionName(ui) : ui.SectionName;
            bool showEyebrow = !string.IsNullOrEmpty(sectionName);
            _view.EyebrowRow.EnableInClassList("hidden", !showEyebrow);
            if (showEyebrow)
            {
                _view.EyebrowLabel.text =
                    $"{sectionName.ToUpperInvariant()} · SECTION {ui.SectionNumber:00}";
                if (theme != null)
                {
                    _view.EyebrowLabel.style.color = theme.Accent;
                    _view.EyebrowDot.style.backgroundColor = theme.Accent;
                }
            }

            var title = new Label(ui.Title ?? string.Empty);
            title.AddToClassList("ui-showcase-title");
            if (theme != null) title.style.color = theme.Ink;
            _host.Add(title);

            if (!string.IsNullOrEmpty(ui.Kicker))
            {
                var kicker = new Label(ui.Kicker);
                kicker.AddToClassList("ui-showcase-kicker");
                if (theme != null) kicker.style.color = theme.InkMuted;
                _host.Add(kicker);
            }
        }

        private void DrawGrid(UiShowcaseSlideSO ui, PresentationThemeSO theme)
        {
            var grid = new VisualElement { name = "ui-grid" };
            grid.AddToClassList("ui-grid");
            _host.Add(grid);

            int columns = Mathf.Max(2, ui.Columns);
            VisualElement row = null;

            for (int i = 0; i < ui.Panels.Count; i++)
            {
                if (i % columns == 0)
                {
                    row = new VisualElement();
                    row.AddToClassList("ui-grid-row");
                    grid.Add(row);
                }

                UiPanelEntrySO entry = ui.Panels[i];
                var cell = new VisualElement();
                cell.AddToClassList("ui-grid-cell");

                var frame = new VisualElement();
                frame.AddToClassList("ui-tile");
                if (theme != null) frame.style.borderTopColor = theme.Hairline;

                RenderTexture rt = _panels != null ? _panels.GetTexture(entry) : null;
                if (rt != null)
                {
                    frame.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
                    frame.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                }
                else
                {
                    // Named placeholder rather than an empty box, so a missing panel is obvious in
                    // rehearsal instead of looking like a rendering fault.
                    var missing = new Label(entry != null ? entry.DisplayName + "\n(no panel)" : "(empty)");
                    missing.AddToClassList("ui-tile-missing");
                    frame.Add(missing);
                }

                var caption = new Label(entry != null ? entry.DisplayName : "—");
                caption.AddToClassList("ui-tile-caption");
                if (theme != null) caption.style.color = theme.InkMuted;

                cell.Add(frame);
                cell.Add(caption);
                row.Add(cell);
            }

            // Pad the last row so remaining tiles keep their column width instead of stretching.
            int remainder = ui.Panels.Count % columns;
            if (remainder != 0 && row != null)
            {
                for (int p = remainder; p < columns; p++)
                {
                    var filler = new VisualElement();
                    filler.AddToClassList("ui-grid-cell");
                    row.Add(filler);
                }
            }
        }

        private void DrawFocus(UiShowcaseSlideSO ui, PresentationThemeSO theme)
        {
            UiPanelEntrySO entry = ui.FocusPanel;

            var wrap = new VisualElement();
            wrap.AddToClassList("ui-focus-wrap");
            _host.Add(wrap);

            var frame = new VisualElement();
            frame.AddToClassList("ui-focus-frame");

            RenderTexture rt = _panels != null ? _panels.GetTexture(entry) : null;
            if (rt != null)
            {
                frame.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
                frame.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            }
            else
            {
                var missing = new Label(entry != null ? entry.DisplayName + " — no panel assigned" : "no panel");
                missing.AddToClassList("ui-tile-missing");
                frame.Add(missing);
            }
            wrap.Add(frame);
            _zoomTargets.Add(frame);

            if (entry != null && !string.IsNullOrEmpty(entry.Caption))
            {
                var side = new VisualElement();
                side.AddToClassList("ui-focus-side");

                var rule = new VisualElement();
                rule.AddToClassList("ui-focus-rule");
                if (theme != null) rule.style.backgroundColor = theme.Accent;
                side.Add(rule);

                var caption = new Label(entry.Caption);
                caption.AddToClassList("ui-focus-caption");
                if (theme != null) caption.style.color = theme.InkMuted;
                side.Add(caption);

                wrap.Add(side);
                _zoomTargets.Add(side);
                side.AddToClassList("ui-zoom--delayed");
            }

            StartZoom();
        }

        private readonly System.Collections.Generic.List<VisualElement> _zoomTargets =
            new System.Collections.Generic.List<VisualElement>();

        /// <summary>
        /// Arms the zoom, then triggers it on the NEXT layout pass.
        ///
        /// USS transitions only animate a property that CHANGES, so adding the resting and arrived classes
        /// in the same frame produces no motion at all — the element is simply born arrived. Scheduling
        /// the second class a frame later is what makes it move. Same trick as the poster reveal.
        /// </summary>
        private void StartZoom()
        {
            foreach (VisualElement e in _zoomTargets) e.AddToClassList("ui-zoom");

            _host.schedule.Execute(() =>
            {
                foreach (VisualElement e in _zoomTargets)
                    if (e != null && e.panel != null) e.AddToClassList("ui-zoom-in");
            }).StartingIn(32);
        }
    }
}
