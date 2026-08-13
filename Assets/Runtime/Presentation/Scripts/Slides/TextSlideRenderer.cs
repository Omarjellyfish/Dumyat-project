using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Presents a <see cref="TextSlideSO"/> by showing and hiding parts of the shared slide tree.
    /// Five layouts, one tree, one component — which is the whole point of the layout enum.
    /// </summary>
    public sealed class TextSlideRenderer : MonoBehaviour, ISlideRenderer
    {
        [SerializeField] private PresentationView _view;

        private void Awake()
        {
            if (_view == null) _view = GetComponentInParent<PresentationView>();
            if (_view == null)
                Debug.LogWarning("[TextSlideRenderer] No PresentationView assigned or found in parents — " +
                                 "text slides will not render.", this);
        }

        public bool CanRender(SlideSO slide) => slide is TextSlideSO;

        public void Hide()
        {
            if (_view == null || !_view.IsReady) return;
            _view.TextSlide.AddToClassList("hidden");
        }

        public void Show(SlideSO slide, PresentationDeckSO deck)
        {
            if (_view == null || !_view.IsReady) return;
            if (!(slide is TextSlideSO text))
            {
                Debug.LogWarning($"[TextSlideRenderer] asked to render {slide?.GetType().Name ?? "null"}.", this);
                return;
            }

            _view.TextSlide.RemoveFromClassList("hidden");
            ApplyTheme(deck != null ? deck.Theme : null);
            ResetPoster();

            TextSlideLayout layout = text.Layout;
            bool isDivider = layout == TextSlideLayout.SectionDivider;
            bool isTitleOnly = layout == TextSlideLayout.TitleOnly;

            // Vertical centring is the divider's and title-only's business; the rest sit at the top.
            _view.TextSlide.EnableInClassList("centred", isDivider || isTitleOnly);

            // ---- eyebrow / numeral / rule ----
            SetVisible(_view.DividerNumeral, isDivider);
            if (isDivider)
                _view.DividerNumeral.text = slide.SectionNumber.ToString("00");

            string sectionName = deck != null ? deck.ResolveSectionName(slide) : slide.SectionName;
            bool showEyebrow = !isTitleOnly && !string.IsNullOrEmpty(sectionName);
            SetVisible(_view.EyebrowRow, showEyebrow);
            if (showEyebrow)
            {
                // Authored uppercase here because USS has no text-transform.
                _view.EyebrowLabel.text = isDivider
                    ? sectionName.ToUpperInvariant()
                    : $"{sectionName.ToUpperInvariant()} · SECTION {slide.SectionNumber:00}";
            }

            SetVisible(_view.Rule, !isDivider && !isTitleOnly);

            // ---- title ----
            _view.Title.text = slide.Title ?? string.Empty;
            _view.Title.EnableInClassList("huge", isDivider);
            _view.Title.EnableInClassList("dense",
                layout == TextSlideLayout.Bullets ||
                layout == TextSlideLayout.TwoColumn ||
                (layout == TextSlideLayout.TitleAndBody && !string.IsNullOrEmpty(text.Body)));

            // ---- kicker ----
            bool hasKicker = !string.IsNullOrEmpty(slide.Kicker);
            SetVisible(_view.Kicker, hasKicker);
            if (hasKicker) _view.Kicker.text = slide.Kicker;

            // ---- body ----
            bool hasBody = layout == TextSlideLayout.TitleAndBody && !string.IsNullOrEmpty(text.Body);
            SetVisible(_view.Body, hasBody);
            if (hasBody) _view.Body.text = text.Body;

            // ---- bullets ----
            bool oneColumn = layout == TextSlideLayout.Bullets;
            bool twoColumn = layout == TextSlideLayout.TwoColumn;
            SetVisible(_view.Bullets, oneColumn);
            SetVisible(_view.BulletsTwo, twoColumn);

            if (oneColumn)
            {
                BuildBullets(_view.Bullets, text.Bullets, 0, text.Bullets.Count);
            }
            else if (twoColumn)
            {
                int half = (text.Bullets.Count + 1) / 2;   // odd counts put the extra row on the left
                BuildBullets(_view.BulletsLeft, text.Bullets, 0, half);
                BuildBullets(_view.BulletsRight, text.Bullets, half, text.Bullets.Count);
            }

            // ---- readouts ----
            bool hasReadouts = text.Readouts.Count > 0;
            SetVisible(_view.Readouts, hasReadouts);
            if (hasReadouts) BuildReadouts(_view.Readouts, text.Readouts, deck != null ? deck.Theme : null);

            // ---- poster ----
            _view.TextSlide.EnableInClassList("poster", text.PosterTreatment);
            if (text.PosterTreatment) StartPoster();
        }

        /// <summary>
        /// Clears the poster classes so a return to slide 1 replays the reveal rather than showing an
        /// already-arrived title.
        /// </summary>
        private void ResetPoster()
        {
            if (_posterRule != null) { _posterRule.RemoveFromHierarchy(); _posterRule = null; }
            _view.Title.RemoveFromClassList("poster-reveal");
            _view.Title.RemoveFromClassList("poster-in");
            _view.Kicker.RemoveFromClassList("poster-reveal");
            _view.Kicker.RemoveFromClassList("poster-reveal--delayed");
            _view.Kicker.RemoveFromClassList("poster-in");
        }

        /// <summary>
        /// Arms the reveal, then triggers it on the NEXT layout pass.
        ///
        /// USS transitions only animate when a property CHANGES, so adding both the resting and arrived
        /// classes in the same frame produces no motion at all — the element is simply born arrived.
        /// Scheduling the second class one frame later is what makes it move.
        /// </summary>
        private void StartPoster()
        {
            _view.Title.AddToClassList("poster-reveal");
            _view.Kicker.AddToClassList("poster-reveal");
            _view.Kicker.AddToClassList("poster-reveal--delayed");

            _posterRule = new VisualElement();
            _posterRule.AddToClassList("poster-rule");
            _view.TextSlide.Add(_posterRule);

            _view.TextSlide.schedule.Execute(() =>
            {
                _view.Title.AddToClassList("poster-in");
                _view.Kicker.AddToClassList("poster-in");
                if (_posterRule != null) _posterRule.AddToClassList("poster-in");
            }).StartingIn(32);
        }

        private VisualElement _posterRule;

        private void ApplyTheme(PresentationThemeSO theme)
        {
            if (theme == null) return;
            _view.Title.style.color = theme.Ink;
            _view.Body.style.color = theme.Ink;
            _view.Kicker.style.color = theme.InkMuted;
            _view.EyebrowLabel.style.color = theme.Accent;
            _view.EyebrowDot.style.backgroundColor = theme.Accent;
            _view.Rule.style.backgroundColor = theme.Hairline;
        }

        /// <summary>
        /// Rows are rebuilt rather than pooled: this happens once per slide change behind a covered
        /// screen, so a handful of allocations is cheaper than the bookkeeping a pool would need.
        /// </summary>
        private static void BuildBullets(VisualElement host, IReadOnlyList<string> items, int from, int to)
        {
            host.Clear();
            for (int i = from; i < to && i < items.Count; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("bullet-row");
                if (i == to - 1) row.AddToClassList("last");

                var index = new Label((i + 1).ToString("00"));
                index.AddToClassList("bullet-index");

                var label = new Label(items[i]);
                label.AddToClassList("bullet-text");

                row.Add(index);
                row.Add(label);
                host.Add(row);
            }
        }

        private static void BuildReadouts(VisualElement host, IReadOnlyList<SlideReadout> readouts,
                                          PresentationThemeSO theme)
        {
            host.Clear();
            foreach (SlideReadout r in readouts)
            {
                var group = new VisualElement();
                group.AddToClassList("readout");

                var label = new Label((r.Label ?? string.Empty).ToUpperInvariant());
                label.AddToClassList("readout-label");

                var value = new Label(r.Value ?? string.Empty);
                value.AddToClassList("readout-value");
                if (theme != null)
                {
                    label.style.color = theme.InkMuted;
                    value.style.color = r.Positive ? theme.Positive : theme.Ink;
                }

                group.Add(label);
                group.Add(value);
                host.Add(group);
            }
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element == null) return;
            element.EnableInClassList("hidden", !visible);
        }
    }
}
