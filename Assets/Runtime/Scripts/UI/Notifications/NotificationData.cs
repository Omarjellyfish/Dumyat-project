using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// One notification's content, handed to <see cref="INotificationService.Enqueue"/>. Immutable value.
    /// <see cref="Icon"/> may be null — the view then shows a plain accent badge. <see cref="Score"/> is
    /// 0-100 (the sale success rate); a negative value hides the score chip/badge for non-sale notifications.
    /// </summary>
    public readonly struct NotificationData
    {
        public readonly string Title;
        public readonly string Description;
        public readonly Sprite Icon;
        public readonly Color Accent;
        public readonly int Score;

        public NotificationData(string title, string description, Sprite icon, Color accent, int score = -1)
        {
            Title = title;
            Description = description;
            Icon = icon;
            Accent = accent;
            Score = score;
        }
    }
}
