using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DumyatRuntime
{
    /// <summary>
    /// Vertical "achievement medal" notification: a "SALE COMPLETE" kicker, an accent-ringed medal disc with
    /// the icon and score badge, the rank title, description, and a bottom countdown bar. Matches the approved
    /// design mockup. Built procedurally for a world-space canvas.
    /// </summary>
    public sealed class NotificationMedalView : NotificationView
    {
        public override void Build(NotificationData data)
        {
            ClearCard();

            // Grown from 360x306 for the same reason as the toast: at 1.6 m the old 19-unit
            // description subtended 1.16°, under what a headset resolves cleanly.
            Card = NotifUI.Rect(Parent, "MedalCard");
            NotifUI.Anchor(Card, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(430f, 370f));
            Group = Card.gameObject.AddComponent<CanvasGroup>();

            var bg = NotifUI.Image(Card, "BG", NotifUI.Rounded(14), NotifUI.Glass, true);
            NotifUI.Fill(bg.rectTransform);

            var kicker = NotifUI.Label(Card, "Kicker", data.Kicker, 21f, NotifUI.Muted,
                TextAlignmentOptions.Center, bold: true);

            // Letter-spacing is an English device. Arabic is a joined script: prising the letters
            // apart breaks the connections the shaper just built, so the kicker only gets it in
            // Latin. ("QUEST COMPLETE" is tracked out; "اكتملت المهمة" is not.)
            if (!ArabicTextShaper.ContainsArabic(data.Kicker)) kicker.characterSpacing = 10f;

            NotifUI.Anchor(kicker.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -16f), new Vector2(380f, 26f));

            // Disc container (everything centred inside).
            var disc = NotifUI.Rect(Card, "Disc");
            NotifUI.Anchor(disc, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f),
                new Vector2(136f, 136f));

            AddCircle(disc, "Glow", 136, new Color(data.Accent.r, data.Accent.g, data.Accent.b, 0.18f));
            AddCircle(disc, "Ring", 120, data.Accent);
            AddCircle(disc, "Face", 112, NotifUI.Mix(data.Accent, NotifUI.Ground, 0.80f));

            if (data.Icon != null)
            {
                var icon = NotifUI.Image(disc, "Icon", data.Icon, Color.white, false);
                icon.preserveAspect = true;
                float sz = 66f * IconScale;   // design default 66px inside the 112px face; IconScale tunes it
                NotifUI.Anchor(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(sz, sz));
            }

            if (data.Score >= 0)
            {
                var scoreBadge = NotifUI.Image(disc, "ScoreBadge", NotifUI.Rounded(11), data.Accent, true);
                NotifUI.Anchor(scoreBadge.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, 6f), new Vector2(50f, 25f));
                var st = NotifUI.Label(scoreBadge.transform, "Txt", data.Score.ToString(), 21f,
                    NotifUI.Ground, TextAlignmentOptions.Center, bold: true);
                NotifUI.Fill(st.rectTransform);
            }

            var rank = NotifUI.Label(Card, "Rank", data.Title, 38f, data.Accent,
                TextAlignmentOptions.Center, bold: true, outline: true, autoSizeMin: 30f);
            NotifUI.Anchor(rank.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -196f), new Vector2(390f, 48f));

            var desc = NotifUI.Label(Card, "Desc", data.Description, 27f, NotifUI.Muted,
                TextAlignmentOptions.Top, bold: false, outline: false, autoSizeMin: 23f);
            NotifUI.Anchor(desc.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -250f), new Vector2(370f, 92f));

            BuildProgressBar(data.Accent);

            Card.gameObject.SetActive(true);
        }

        private static void AddCircle(Transform parent, string name, int diameter, Color color)
        {
            var img = NotifUI.Image(parent, name, NotifUI.Circle(diameter), color, false);
            NotifUI.Anchor(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(diameter, diameter));
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
            ProgressFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        }
    }
}
