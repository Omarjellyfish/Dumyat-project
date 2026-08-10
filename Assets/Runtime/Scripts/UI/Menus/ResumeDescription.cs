using System;
using System.Globalization;

namespace DumyatRuntime
{
    /// <summary>
    /// Turns a resume point into the line shown under a Continue control — "Ras El Bar · 3 h ago".
    ///
    /// Shared because there are now two Continues: the UI Toolkit button on the main menu and the
    /// nail on the carpentry bench. They must answer "where did I leave off" identically, and a
    /// second copy of the time-ago rules would drift the first time one of them was tweaked.
    ///
    /// THE PLACE NAME. What is saved is a scene name — a build identifier, never translated, and
    /// historically shown raw: the line used to read "El_manara · 3 h ago", and after the scenes
    /// were renamed it would have read "RasElBar". <paramref name="catalog"/> turns that into the
    /// bilingual name the player knows, which for every place the bus serves now comes from that
    /// stop's own <see cref="TravelDestination"/> rather than a second list. Resolving it HERE
    /// rather than in each caller is the same reason the time-ago rules live here.
    ///
    /// Static because it is a pure function of its arguments — the catalogue is data, not a service,
    /// so nothing here needs a container.
    /// </summary>
    public static class ResumeDescription
    {
        /// <summary>
        /// Where the player left off, plus how long ago when that can be worked out. Never null.
        ///
        /// Falls back to the raw scene name whenever no catalogue is supplied or nothing names the
        /// scene — an identifier on the button beats an empty one.
        /// </summary>
        public static string For(string sceneName, string savedAtUtc,
                                 MenuCatalogSO catalog = null, bool arabic = false)
        {
            if (string.IsNullOrEmpty(sceneName)) return string.Empty;

            // Where Continue will ACTUALLY go, which is not always where the save says. Under a fixed
            // Continue target the button lands somewhere else entirely, and naming the recorded scene
            // here would promise a destination the button does not honour.
            string destination = catalog != null ? catalog.ContinueSceneFor(sceneName) : sceneName;

            string placeName = catalog != null ? catalog.SceneLabelFor(destination, arabic) : destination;
            if (string.IsNullOrWhiteSpace(placeName)) placeName = destination;

            if (string.IsNullOrEmpty(savedAtUtc)) return placeName;

            if (!DateTime.TryParse(savedAtUtc, CultureInfo.InvariantCulture,
                                   DateTimeStyles.RoundtripKind, out DateTime saved))
                return placeName;

            TimeSpan ago = DateTime.UtcNow - saved;

            // A clock skew or an edited save can put the stamp in the future; showing the place
            // alone beats showing "in -3 hours".
            if (ago < TimeSpan.Zero) return placeName;

            // NOTE: the suffix is English in both languages. The place name translates, the
            // "3 h ago" does not — worth finishing when the rest of the menu chrome is localised.
            if (ago.TotalMinutes < 2) return $"{placeName} · just now";
            if (ago.TotalHours < 1) return $"{placeName} · {(int)ago.TotalMinutes} min ago";
            if (ago.TotalDays < 1) return $"{placeName} · {(int)ago.TotalHours} h ago";

            return $"{placeName} · {(int)ago.TotalDays} d ago";
        }
    }
}
