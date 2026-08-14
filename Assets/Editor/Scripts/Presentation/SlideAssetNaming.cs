#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using DumyatRuntime;
using UnityEditor;
using UnityEngine;

namespace DumyatEditor
{
    /// <summary>
    /// Keeps slide FILENAMES in step with the deck's running order.
    ///
    /// WHY THE FILENAME CARRIES THE NUMBER AT ALL:
    /// the deck's order lives in <see cref="PresentationDeckSO"/>, and that is the only order the
    /// projector obeys. But 55 slide assets sorted alphabetically in the Project window bore no relation
    /// to the talk, so finding "the slide after the UI divider" meant opening the deck and reading GUIDs.
    /// Numbering the files makes the Project window agree with the running order for free.
    ///
    /// WHICH ONE IS THE TRUTH: the DECK is. A filename is a label, and this class exists precisely
    /// because a label goes stale — reorder in the deck inspector, press "Renumber Assets To Deck Order",
    /// and the labels catch up. Nothing at runtime reads a slide by name, so a stale number is only ever
    /// cosmetic; that is why this is a button and not something that fires on every edit.
    ///
    /// Renaming is GUID-safe: the .meta travels with the .asset, so the deck, and anything else pointing
    /// at a slide, keeps pointing at it.
    /// </summary>
    public static class SlideAssetNaming
    {
        public const string Prefix = "Slide_";

        /// <summary>Two-phase renaming parks assets here so a swap cannot collide mid-flight.</summary>
        private const string TempPrefix = "__slide_renumber_";

        // ---- naming ---------------------------------------------------------------------------

        /// <summary>
        /// The part of a slide's name that is NOT its running number — "Art_Home_fan" out of
        /// "Slide_07_Art_Home_fan".
        ///
        /// Stripping only a LEADING ordinal token is what makes this idempotent: run it on an
        /// already-numbered name and you get the same descriptor back, so renumbering twice is a no-op
        /// rather than a name that grows a number each time. A token of letters alone ("Sprint", "Ui")
        /// is a name and is kept — which is why "Slide_46_Sprint_1" keeps its meaningful trailing 1.
        /// </summary>
        public static string Descriptor(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return "Slide";

            string rest = assetName.StartsWith(Prefix, StringComparison.Ordinal)
                ? assetName.Substring(Prefix.Length)
                : assetName;

            int cut = rest.IndexOf('_');
            if (cut > 0 && IsOrdinalToken(rest.Substring(0, cut)))
                rest = rest.Substring(cut + 1);

            return rest.Length == 0 ? "Slide" : rest;
        }

        /// <summary>"01", "12", "A01" — a leading letter is allowed because the deck once numbered the
        /// Shipyard run A01/A02/A03. Letters alone are a name, not an ordinal.</summary>
        private static bool IsOrdinalToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;

            int i = 0;
            if (char.IsLetter(token[i])) i++;
            if (i >= token.Length) return false;

            for (; i < token.Length; i++)
                if (!char.IsDigit(token[i])) return false;

            return true;
        }

        /// <summary>The name a slide should carry at a 1-based position in the deck.</summary>
        public static string NumberedName(string assetName, int position) =>
            Prefix + position.ToString("00") + "_" + Descriptor(assetName);

        // ---- the operation --------------------------------------------------------------------

        private readonly struct Planned
        {
            public readonly SlideSO Slide;
            public readonly string OldName;
            public readonly string NewName;

            public Planned(SlideSO slide, string oldName, string newName)
            {
                Slide = slide;
                OldName = oldName;
                NewName = newName;
            }
        }

        /// <summary>
        /// Renames every slide asset the deck references so its filename leads with its 1-based deck
        /// position. Slides not in the deck are left alone — they have no position to carry.
        ///
        /// Pass <paramref name="apply"/> false for a report of what would change without touching disk.
        /// </summary>
        public static string RenumberToDeckOrder(PresentationDeckSO deck, bool apply)
        {
            if (deck == null) return "[SlideAssetNaming] no deck.";

            List<Planned> planned = Plan(deck, out int holes);

            var log = new StringBuilder(apply
                ? $"[SlideAssetNaming] renumbered slide assets to '{deck.name}' order:\n"
                : $"[SlideAssetNaming] renumber preview for '{deck.name}':\n");

            if (planned.Count == 0)
            {
                log.AppendLine("   every slide filename already matches its deck position.");
                if (holes > 0) log.AppendLine($"   {holes} empty slot(s) in the deck still consumed a number.");
                return log.ToString();
            }

            foreach (Planned p in planned)
                log.AppendLine("   " + p.OldName.PadRight(38) + " → " + p.NewName);

            if (!apply)
            {
                log.AppendLine($"   {planned.Count} rename(s) pending. Nothing written.");
                return log.ToString();
            }

            int done = Rename(planned, log);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.AppendLine($"   {done}/{planned.Count} renamed. GUIDs are untouched, so the deck and " +
                           "every other reference still resolve.");
            if (holes > 0)
                log.AppendLine($"   note: {holes} empty slot(s) in the deck each consumed a number, so " +
                               "the files match the inspector's row numbers. Validate Deck to find them.");
            return log.ToString();
        }

        private static List<Planned> Plan(PresentationDeckSO deck, out int holes)
        {
            var planned = new List<Planned>();
            var numbered = new HashSet<SlideSO>();
            holes = 0;

            for (int i = 0; i < deck.Count; i++)
            {
                SlideSO slide = deck.SlideAt(i);

                // An empty slot still consumes a number so the filenames line up with the row numbers
                // the deck inspector shows. The validator is what complains about the hole itself.
                if (slide == null) { holes++; continue; }

                // A slide reused for a recap appears twice; it is one file, numbered by where it first
                // shows up. Numbering it twice would just be the second position overwriting the first.
                if (!numbered.Add(slide)) continue;

                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(slide))) continue;

                string want = NumberedName(slide.name, i + 1);
                if (want != slide.name) planned.Add(new Planned(slide, slide.name, want));
            }

            return planned;
        }

        /// <summary>
        /// Renames in two passes, via throwaway names.
        ///
        /// A single pass breaks on the ordinary case of two slides swapping places: renaming 12→13 fails
        /// while the old 13 still holds that name, and Unity resolves the clash by silently appending a
        /// number rather than by refusing. Parking everything first makes every target free.
        ///
        /// Deliberately NOT wrapped in StartAssetEditing: batching defers the path bookkeeping that pass
        /// two depends on, which is exactly the ordering this needs to be able to trust.
        /// </summary>
        private static int Rename(List<Planned> planned, StringBuilder log)
        {
            for (int i = 0; i < planned.Count; i++)
            {
                string path = AssetDatabase.GetAssetPath(planned[i].Slide);
                string error = AssetDatabase.RenameAsset(path, TempPrefix + i);
                if (!string.IsNullOrEmpty(error))
                    log.AppendLine($"   ! parking '{planned[i].OldName}' failed: {error}");
            }

            int done = 0;
            foreach (Planned p in planned)
            {
                string path = AssetDatabase.GetAssetPath(p.Slide);
                string error = AssetDatabase.RenameAsset(path, p.NewName);

                if (string.IsNullOrEmpty(error)) done++;
                else log.AppendLine($"   ! '{p.OldName}' → '{p.NewName}' failed: {error}");
            }

            return done;
        }

        // ---- menu -----------------------------------------------------------------------------

        [MenuItem("Dumyat/Presentation/Renumber Slide Assets To Deck Order", priority = 41)]
        private static void RenumberMenu()
        {
            PresentationDeckSO deck = DeckValidator.ResolveDeck();
            if (deck == null)
            {
                Debug.LogError("[SlideAssetNaming] No PresentationDeckSO found. Select one first.");
                return;
            }

            string preview = RenumberToDeckOrder(deck, false);
            if (!EditorUtility.DisplayDialog("Renumber slide assets",
                    preview + "\nFilenames only — GUIDs, and therefore every reference, are untouched.",
                    "Rename", "Cancel"))
                return;

            Debug.Log(RenumberToDeckOrder(deck, true), deck);
        }
    }
}
#endif
