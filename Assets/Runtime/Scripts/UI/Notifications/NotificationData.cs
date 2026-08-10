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
    /// <see cref="CoalesceKey"/> collapses a burst of updates about the same thing into one card.
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

        /// <summary>
        /// Identifies a REPLACEABLE notification: a later card with the same key supersedes an earlier
        /// one that is still queued or still on screen, instead of being shown after it.
        ///
        /// This exists because progress is reported per unit of work while the player experiences it as
        /// one action. Selling four fish in a single transaction advances the quest four times, which
        /// without a key means four 7-second cards reading "1 / 5", "2 / 5", "3 / 5", "4 / 5" — nearly
        /// half a minute of HUD for one hand-over, and the number the player actually wants is the last
        /// one. Keyed, they collapse to a single card showing "4 / 5".
        ///
        /// Null/empty (the default) means "always show me" — every card is its own event. Keep it that
        /// way for anything the player earned separately, e.g. two sales are two results even when they
        /// happen seconds apart. Scope the key to the subject, not the sender:
        /// <c>$"quest-progress:{questID}"</c>, so two different quests ticking still get a card each.
        /// </summary>
        public readonly string CoalesceKey;

        /// <summary>
        /// Steps done and steps needed, for a card reporting progress toward something countable —
        /// 4 and 5 for "four of the five fish are sold". <see cref="Target"/> of 0 or less means this
        /// is not a progress card and the view draws its plain layout, exactly the way a negative
        /// <see cref="Score"/> hides the score chip.
        ///
        /// The pair is carried as NUMBERS rather than baked into <see cref="Description"/> because the
        /// count is the thing the player looks at: as data the card can set it large, right-aligned and
        /// in tabular figures, and draw it as segments that read before the digits do. As prose it can
        /// only ever be one more grey sentence.
        /// </summary>
        public readonly int Current;
        public readonly int Target;

        /// <summary>True when <see cref="Current"/>/<see cref="Target"/> describe a real count.</summary>
        public bool HasProgress => Target > 0 && Current >= 0;

        /// <summary>Default kicker, kept so existing sale notifications read exactly as before.</summary>
        public const string DefaultKicker = "SALE COMPLETE";

        /// <summary>
        /// A toast reporting <paramref name="current"/> of <paramref name="target"/> steps done.
        /// <paramref name="objective"/> is what the player still has to do, in their own terms
        /// ("Hand fish to Am Sayed") — NOT a restatement of the count, which the card draws itself.
        /// </summary>
        public static NotificationData Progress(string title, string objective, Sprite icon, Color accent,
            int current, int target, string coalesceKey) =>
            new NotificationData(title, objective, icon, accent, -1, NotificationStyle.Toast, null,
                                 coalesceKey, current, target);

        public NotificationData(string title, string description, Sprite icon, Color accent, int score = -1)
            : this(title, description, icon, accent, score, NotificationStyle.Medal, DefaultKicker)
        {
        }

        public NotificationData(string title, string description, Sprite icon, Color accent, int score,
            NotificationStyle style, string kicker, string coalesceKey = null,
            int current = -1, int target = -1)
        {
            Title = title;
            Description = description;
            Icon = icon;
            Accent = accent;
            Score = score;
            Style = style;
            Kicker = string.IsNullOrEmpty(kicker) ? DefaultKicker : kicker;
            CoalesceKey = coalesceKey;
            Current = current;
            Target = target;
        }
    }
}
