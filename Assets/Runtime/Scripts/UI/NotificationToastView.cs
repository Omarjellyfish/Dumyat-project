using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DumyatRuntime
{
    /// <summary>
    /// Horizontal "toast card" notification: left accent bar, icon badge, rank title + score chip, description,
    /// and a bottom countdown bar. Matches the approved design mockup. Built procedurally so it renders crisp
    /// in a world-space canvas with no imported sprites.
    /// </summary>
    public sealed class NotificationToastView : NotificationView
    {
        public override void Build(NotificationData data)
        {
            ClearCard();

            // Card: top-right of the canvas, fixed size (metres = size * manager canvasScale).
            Card = NotifUI.Rect(Parent, "ToastCard");
            NotifUI.Anchor(Card, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(440f, 118f));
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
                new Vector2(18f, 0f), new Vector2(52f, 52f));
            var badgeInner = NotifUI.Image(badge.transform, "Inner", NotifUI.Rounded(11),
                NotifUI.Mix(data.Accent, NotifUI.Ground, 0.74f), true);
            NotifUI.Fill(badgeInner.rectTransform, 2, 2, 2, 2);
            if (data.Icon != null)
            {
                var icon = NotifUI.Image(badgeInner.transform, "Icon", data.Icon, Color.white, false);
                icon.preserveAspect = true;
                float sz = 34f * IconScale;   // design default ~34px inside the 52px badge; IconScale tunes it
                NotifUI.Anchor(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(sz, sz));
            }

            // Body block, right of the badge, above the countdown bar.
            var body = NotifUI.Rect(Card, "Body");
            NotifUI.Fill(body, 84f, 14f, 16f, 22f);

            var title = NotifUI.Label(body, "Rank", data.Title, 28f, data.Accent,
                TextAlignmentOptions.TopLeft, bold: true, outline: true);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f); titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.offsetMin = new Vector2(0f, -34f); titleRt.offsetMax = new Vector2(-92f, 0f);

            if (data.Score >= 0)
            {
                var chip = NotifUI.Image(body, "ScoreChip", NotifUI.Rounded(12),
                    NotifUI.Mix(data.Accent, NotifUI.Glass, 0.78f), true);
                NotifUI.Anchor(chip.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, -1f), new Vector2(86f, 27f));
                var chipTx = NotifUI.Label(chip.transform, "Txt", $"{data.Score}<alpha=#88>/100", 17f,
                    data.Accent, TextAlignmentOptions.Center, bold: true);
                NotifUI.Fill(chipTx.rectTransform);
            }

            var desc = NotifUI.Label(body, "Desc", data.Description, 19f, NotifUI.Muted,
                TextAlignmentOptions.TopLeft, bold: false);
            var descRt = desc.rectTransform;
            descRt.anchorMin = new Vector2(0f, 0f); descRt.anchorMax = new Vector2(1f, 1f);
            descRt.offsetMin = Vector2.zero; descRt.offsetMax = new Vector2(0f, -40f);

            BuildProgressBar(data.Accent);

            Card.gameObject.SetActive(true);
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
