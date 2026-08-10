using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatRuntime
{
    /// <summary>
    /// The name a place should be CALLED, as opposed to the name its scene file happens to have.
    ///
    /// Scene names are build artefacts — "RasElBar", "EzbetElBorg", "CarpentryQuarter". They are not
    /// written for a player: no spaces, no Arabic, and no guarantee they survive a rename. The towns
    /// already have proper names, authored once on the <see cref="TravelDestination"/> assets the bus
    /// line is built from, in both languages. This reads them from there so the pause menu and anything
    /// else that must name the place agree with the departures board the player just read.
    ///
    /// The destinations are found through the scene's <see cref="BusStation"/>, whose authored list
    /// deliberately includes its own town (it is only filtered out of what the station will SELL). Every
    /// town on the line has a stop, so every town can name itself; anywhere else falls back to the scene
    /// name, which is no worse than what it replaced.
    /// </summary>
    public static class SceneDisplayName
    {
        /// <summary>
        /// The display name of the active scene, in the player's language, or the raw scene name when
        /// this place is not a town on the bus line.
        /// </summary>
        public static string ForActiveScene(bool arabic = false)
        {
            string sceneName = SceneManager.GetActiveScene().name;
            return For(sceneName, arabic);
        }

        /// <summary>The display name for a given scene name, or that scene name if nothing knows it.</summary>
        public static string For(string sceneName, bool arabic = false)
        {
            if (string.IsNullOrWhiteSpace(sceneName)) return sceneName;

            // Inactive included: a stop switched off for a beat still carries the authored route list.
            var stations = UnityEngine.Object.FindObjectsByType<BusStation>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var station in stations)
            {
                var destinations = station != null ? station.Destinations : null;
                if (destinations == null) continue;

                for (int i = 0; i < destinations.Count; i++)
                {
                    TravelDestination d = destinations[i];
                    if (d == null) continue;
                    if (!string.Equals(d.SceneName, sceneName, StringComparison.OrdinalIgnoreCase)) continue;

                    string picked = UiLanguage.Pick(arabic, d.DisplayName, d.DisplayNameArabic);
                    if (!string.IsNullOrWhiteSpace(picked)) return picked;
                }
            }

            return sceneName;
        }
    }
}
