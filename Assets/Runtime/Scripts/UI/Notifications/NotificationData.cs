using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>Which card shape a notification is drawn as.</summary>
    public enum NotificationStyle
    {
        /// <summary>Wide, short card. For frequent, low-ceremony beats — a quest ticking forward.</summary>
        Toast,

        /// <summary>Tall card with a medal disc. For the rare, earned moments — a quest completing.</summary>
        Medal
    }

    /// <summary>
    /// One notification's content, handed to <see cref="INotificationService.Enqueue"/>. Immutable value.
    /// <see cref="Icon"/> may be null — the view then shows a plain accent badge. <see cref="Score"/> is
    /// 0-100 (the sale success rate); a negative value hides the score chip/badge for non-sale notifications.
    /// <see cref="Kicker"/> is the small uppercase label above a medal ("SALE COMPLETE", "QUEST COMPLETE").
    /// </summary>
    public readonly struct NotificationData
    {
        public readonly string Title;
        public readonly string Description;
        public readonly Sprite Icon;
        public readonly Color Accent;
        public readonly int Score;
        public readonly NotificationStyle Style;
        public readonly string Kicker;

        /// <summary>Default kicker, kept so existing sale notifications read exactly as before.</summary>
        public const string DefaultKicker = "SALE COMPLETE";

        public NotificationData(string title, string description, Sprite icon, Color accent, int score = -1)
            : this(title, description, icon, accent, score, NotificationStyle.Medal, DefaultKicker)
        {
        }

        public NotificationData(string title, string description, Sprite icon, Color accent, int score,
            NotificationStyle style, string kicker)
        {
            Title = title;
            Description = description;
            Icon = icon;
            Accent = accent;
            Score = score;
            Style = style;
            Kicker = string.IsNullOrEmpty(kicker) ? DefaultKicker : kicker;
        }
    }
}
