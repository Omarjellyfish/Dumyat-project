using System;
using System.Globalization;

namespace DumyatRuntime
{
    /// <summary>
    /// Turns a resume point into the line shown under a Continue control — "El_manara · 3 h ago".
    ///
    /// Shared because there are now two Continues: the UI Toolkit button on the main menu and the
    /// nail on the carpentry bench. They must answer "where did I leave off" identically, and a
    /// second copy of the time-ago rules would drift the first time one of them was tweaked.
    ///
    /// Static because it is a pure function of its arguments — no state, no scene, nothing to inject.
    /// </summary>
    public static class ResumeDescription
    {
        /// <summary>
        /// Scene name, plus how long ago it was left when that can be worked out. Never returns null.
        /// </summary>
        public static string For(string sceneName, string savedAtUtc)
        {
            if (string.IsNullOrEmpty(sceneName)) return string.Empty;
            if (string.IsNullOrEmpty(savedAtUtc)) return sceneName;

            if (!DateTime.TryParse(savedAtUtc, CultureInfo.InvariantCulture,
                                   DateTimeStyles.RoundtripKind, out DateTime saved))
                return sceneName;

            TimeSpan ago = DateTime.UtcNow - saved;

            // A clock skew or an edited save can put the stamp in the future; showing the scene
            // alone beats showing "in -3 hours".
            if (ago < TimeSpan.Zero) return sceneName;

            if (ago.TotalMinutes < 2) return $"{sceneName} · just now";
            if (ago.TotalHours < 1) return $"{sceneName} · {(int)ago.TotalMinutes} min ago";
            if (ago.TotalDays < 1) return $"{sceneName} · {(int)ago.TotalHours} h ago";

            return $"{sceneName} · {(int)ago.TotalDays} d ago";
        }
    }
}
