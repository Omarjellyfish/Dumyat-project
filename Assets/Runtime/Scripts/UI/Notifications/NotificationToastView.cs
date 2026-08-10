using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DumyatRuntime
{
    /// <summary>
    /// Horizontal "toast card" notification: left accent bar, icon badge, rank title + score chip, description,
    /// and a bottom countdown bar. Matches the approved design mockup. Built procedurally so it renders crisp
    /// in a world-space canvas with no imported sprites.
    ///
    /// A card carrying a count (<see cref="NotificationData.HasProgress"/>) draws a second layout: the same
    /// frame, but the count set large and right-aligned as a datum, the description freed to state the
    /// objective, and a row of segments — one per step, lit as they are done. The segments exist because a
    /// notification lives in the top-right of the player's view, which is peripheral vision: shape resolves
    /// there long before digits do, so "nearly finished" reads without the player turning to read "4 / 5".
    /// </summary>
    public sealed class NotificationToastView : NotificationView
    {
        // Above this many steps the segments become slivers and stop being countable at a glance, so the
        // row falls back to one continuous fill. Chosen to match the segment floor below, not by taste:
        // 10 segments across the 434-unit body leaves 36 units each, still clearly separate at 1.6 m.
        private const int MaxSegments = 10;

        // Card heights. The plain card is tall because a quest description can run to three lines; the
        // progress card knows its description is one line, so it does not pay for two it never uses.
        private const float PlainHeight = 190f;
        private const float ProgressHeight = 140f;

        public override void Build(NotificationData data)
        {
            ClearCard();

            // Card: top-right of the canvas, fixed size (metres = size * manager canvasScale).
            //
            // SIZED FOR THE HEADSET, NOT THE MONITOR. At the manager's defaults (canvasScale 0.0017,
            // 1.6 m) one canvas unit subtends 0.061°, so the old 19-unit description came to 1.16° —
            // roughly 23 display pixels per em on a Quest 2, which is where text stops resolving and
            // starts smearing. Body type is now 27 units (1.64°) and the title 36 (2.19°), and the
            // card grew from 440x118 to hold three lines of it rather than letting them spill out.
            float height = data.HasProgress ? ProgressHeight : PlainHeight;
            Card = NotifUI.Rect(Parent, "ToastCard");
            NotifUI.Anchor(Card, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(560f, height));
            Group = Card.gameObject.AddComponent<CanvasGroup>();

            // Ground.
            var bg = NotifUI.Image(Card, "BG", NotifUI.Rounded(14), NotifUI.Glass, true);
            NotifUI.Fill(bg.rectTransform);

            // Left accent bar (inset so it stays within the rounded corners).
            var bar = NotifUI.Image(Card, "AccentBar", NotifUI.Rounded(2), data.Accent, true);
            bar.rectTransform.anchorMin = new Vector2(0f, 0f);
            bar.rectTransform.anchorMax = new Vector2(0f, 1f);
            bar.rectTransform.pivot = new Vector2(0f, 0.5f);
            bar.rectTransform.sizeDelta = new Vector2(5f, -14f);
            bar.rectTransform.anchoredPosition = new Vector2(4f, 0f);

            // Icon badge (2px accent border + tinted inner; the sprite sits inside if provided).
            var badge = NotifUI.Image(Card, "Badge", NotifUI.Rounded(12), data.Accent, true);
            NotifUI.Anchor(badge.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(20f, 0f), new Vector2(72f, 72f));
            var badgeInner = NotifUI.Image(badge.transform, "Inner", NotifUI.Rounded(11),
                NotifUI.Mix(data.Accent, NotifUI.Ground, 0.74f), true);
            NotifUI.Fill(badgeInner.rectTransform, 2, 2, 2, 2);
            if (data.Icon != null)
            {
                var icon = NotifUI.Image(badgeInner.transform, "Icon", data.Icon, Color.white, false);
                icon.preserveAspect = true;
                float sz = 48f * IconScale;   // design default ~48px inside the 72px badge; IconScale tunes it
                NotifUI.Anchor(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(sz, sz));
            }

            // Body block, right of the badge, above the countdown bar.
            var body = NotifUI.Rect(Card, "Body");
            NotifUI.Fill(body, 108f, data.HasProgress ? 14f : 16f, 18f, data.HasProgress ? 20f : 24f);

            // How much of the title row the right-hand element claims. The title is then cut short by
            // exactly that much, which is what keeps the count inside the card instead of the two
            // fighting over the same span and one of them running off the edge.
            float rightReserve = data.HasProgress ? CountWidth : (data.Score >= 0 ? 100f : 0f);

            var title = NotifUI.Label(body, "Rank", data.Title, 36f, data.Accent,
                TextAlignmentOptions.TopLeft, bold: true, outline: true, autoSizeMin: 28f);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f); titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.offsetMin = new Vector2(0f, -44f); titleRt.offsetMax = new Vector2(-rightReserve, 0f);

            if (data.Score >= 0)
            {
                var chip = NotifUI.Image(body, "ScoreChip", NotifUI.Rounded(12),
                    NotifUI.Mix(data.Accent, NotifUI.Glass, 0.78f), true);
                NotifUI.Anchor(chip.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, -1f), new Vector2(98f, 32f));
                var chipTx = NotifUI.Label(chip.transform, "Txt", $"{data.Score}<alpha=#88>/100", 21f,
                    data.Accent, TextAlignmentOptions.Center, bold: true);
                NotifUI.Fill(chipTx.rectTransform);
            }

            if (data.HasProgress) BuildProgressCount(body, data);

            // Description: the objective on a progress card (one line, above the segment row), the
            // free-form quest text otherwise (up to three lines, down to the bottom of the body).
            float descTop = data.HasProgress ? 48f : 50f;
            float descBottom = data.HasProgress ? SegmentRowHeight + SegmentRowGap : 0f;
            var desc = NotifUI.Label(body, "Desc", data.Description, data.HasProgress ? 24f : 27f,
                NotifUI.Muted, TextAlignmentOptions.TopLeft, bold: false, outline: false,
                autoSizeMin: data.HasProgress ? 21f : 23f);
            var descRt = desc.rectTransform;
            descRt.anchorMin = new Vector2(0f, 0f); descRt.anchorMax = new Vector2(1f, 1f);
            descRt.offsetMin = new Vector2(0f, descBottom); descRt.offsetMax = new Vector2(0f, -descTop);

            if (data.HasProgress) BuildSegments(body, data);

            BuildProgressBar(data.Accent);

            Card.gameObject.SetActive(true);
        }

        // --- Progress card pieces -----------------------------------------------

        // Width reserved at the right of the title row for the count. Wide enough for "100/100" at
        // the sizes below, so a long quest ever hitting three digits still lands inside the card.
        private const float CountWidth = 150f;
        private const float SegmentRowHeight = 10f;
        private const float SegmentRowGap = 12f;
        private const float SegmentSpacing = 8f;

        /// <summary>
        /// "4 / 5" as the card's headline datum: top-right of the body, accent, tabular-ish and set
        /// large, with the target dimmed so the eye lands on the number that changed. Anchored to the
        /// body — which already stops 18 units short of the card edge — and right-aligned inside a
        /// fixed <see cref="CountWidth"/> box, so it cannot grow past the card no matter the digits.
        /// </summary>
        private static void BuildProgressCount(RectTransform body, NotificationData data)
        {
            var count = NotifUI.Label(body, "Count", $"{data.Current}<size=62%><alpha=#7F>/{data.Target}",
                46f, data.Accent, TextAlignmentOptions.TopRight, bold: true, outline: true,
                autoSizeMin: 34f);
            var rt = count.rectTransform;
            rt.anchorMin = new Vector2(1f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(CountWidth, 50f);
        }

        /// <summary>
        /// One lit block per step done, one hollow block per step left, along the bottom of the body.
        /// Past <see cref="MaxSegments"/> the blocks are too thin to count, so it draws a single
        /// continuous fill instead — same row, same meaning, no pretence of countability.
        /// </summary>
        private static void BuildSegments(RectTransform body, NotificationData data)
        {
            var row = NotifUI.Rect(body, "Segments");
            row.anchorMin = new Vector2(0f, 0f); row.anchorMax = new Vector2(1f, 0f);
            row.pivot = new Vector2(0.5f, 0f);
            row.sizeDelta = new Vector2(0f, SegmentRowHeight);
            row.anchoredPosition = Vector2.zero;

            int target = data.Target;
            int done = Mathf.Clamp(data.Current, 0, target);
            Color lit = data.Accent;
            Color unlit = new Color(data.Accent.r, data.Accent.g, data.Accent.b, 0.16f);

            if (target > MaxSegments)
            {
                var track = NotifUI.Image(row, "Track", NotifUI.Rounded(5), unlit, true);
                NotifUI.Fill(track.rectTransform);

                var fill = NotifUI.Image(track.transform, "Fill", NotifUI.Rounded(5), lit, true);
                var fillRt = fill.rectTransform;
                fillRt.anchorMin = Vector2.zero;
                fillRt.anchorMax = new Vector2((float)done / target, 1f);
                fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;
                return;
            }

            // Laid out in normalised anchors rather than pixels: the row stretches with the body, so
            // the segments stay flush to both edges without the view knowing the card's width.
            //
            // A step still to do is drawn as an OUTLINE, not a dimmer fill: at 1.6 m a low-alpha fill
            // and a lit one differ only in brightness, which is the first thing a headset's lens
            // flattens. Solid-versus-hollow survives that.
            Color ring = new Color(data.Accent.r, data.Accent.g, data.Accent.b, 0.42f);
            float span = 1f / target;
            for (int i = 0; i < target; i++)
            {
                bool on = i < done;
                var seg = NotifUI.Image(row, $"Seg{i}", NotifUI.Rounded(3), on ? lit : ring, true);
                var rt = seg.rectTransform;
                rt.anchorMin = new Vector2(i * span, 0f);
                rt.anchorMax = new Vector2((i + 1) * span, 1f);
                rt.offsetMin = new Vector2(i == 0 ? 0f : SegmentSpacing * 0.5f, 0f);
                rt.offsetMax = new Vector2(i == target - 1 ? 0f : -SegmentSpacing * 0.5f, 0f);

                if (on) continue;

                // Punches the middle out with the card's own ground, leaving a 2-unit accent ring.
                var hollow = NotifUI.Image(seg.transform, "Hollow", NotifUI.Rounded(2), NotifUI.Glass, true);
                NotifUI.Fill(hollow.rectTransform, 2f, 2f, 2f, 2f);
            }
        }

        private void BuildProgressBar(Color accent)
        {
            var track = NotifUI.Image(Card, "Progress", null, NotifUI.BarBg, false);
            track.rectTransform.anchorMin = new Vector2(0f, 0f);
            track.rectTransform.anchorMax = new Vector2(1f, 0f);
            track.rectTransform.pivot = new Vector2(0.5f, 0f);
            track.rectTransform.sizeDelta = new Vector2(-24f, 4f);
            track.rectTransform.anchoredPosition = new Vector2(0f, 4f);

            ProgressFill = NotifUI.Image(track.transform, "Fill", null, accent, false);
            NotifUI.Fill(ProgressFill.rectTransform);
            ProgressFill.rectTransform.pivot = new Vector2(0f, 0.5f);   // scaleX shrinks toward the left
        }
    }
}
