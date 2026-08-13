#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using DumyatRuntime;
using UnityEditor;
using UnityEngine;

namespace DumyatEditor
{
    /// <summary>
    /// Checks a <see cref="PresentationDeckSO"/> for the mistakes that only show up mid-talk.
    ///
    /// WHY THIS EXISTS: a deck is 58 assets referencing each other by hand. The failure mode is not a
    /// crash, it is a slide that renders blank in front of an audience because a body string was never
    /// filled in, or a number key that jumps nowhere because a section number does not match the
    /// section-name table. None of that is visible from the Inspector without opening all 58.
    ///
    /// Every finding is logged against the asset it belongs to, so clicking the console line selects
    /// the thing to fix.
    /// </summary>
    public static class DeckValidator
    {
        private enum Severity { Error, Warning, Info }

        private readonly struct Finding
        {
            public readonly Severity Level;
            public readonly string Message;
            public readonly Object Context;

            public Finding(Severity level, string message, Object context)
            {
                Level = level;
                Message = message;
                Context = context;
            }
        }

        [MenuItem("Dumyat/Presentation/Validate Deck", priority = 40)]
        private static void ValidateSelected()
        {
            PresentationDeckSO deck = Resolve();
            if (deck == null)
            {
                Debug.LogError("[DeckValidator] No PresentationDeckSO found. Select one, or create it " +
                               "via Assets ▸ Create ▸ Dumyat ▸ Presentation ▸ Deck.");
                return;
            }
            Debug.Log(Validate(deck), deck);
        }

        /// <summary>
        /// Runs every check and returns the report. Findings are additionally logged individually
        /// against the offending asset so console lines are clickable; the returned string is what the
        /// menu item prints, and what a caller can assert on without scraping the console.
        /// </summary>
        public static string Validate(PresentationDeckSO deck)
        {
            var findings = new List<Finding>();

            // ---- deck-level defaults ----
            if (deck.Theme == null)
                findings.Add(new Finding(Severity.Warning,
                    "Deck has no Theme — renderers will fall back to the stylesheet colours.", deck));
            if (deck.DefaultBackground == null)
                findings.Add(new Finding(Severity.Error,
                    "Deck has no Default Background — slides without an override will show a flat panel.", deck));
            if (deck.DefaultTransition == null)
                findings.Add(new Finding(Severity.Error,
                    "Deck has no Default Transition — every move will be an instant cut.", deck));
            if (deck.Count == 0)
                findings.Add(new Finding(Severity.Error, "Deck is empty.", deck));

            // ---- slide-level ----
            var seen = new Dictionary<SlideSO, int>();
            var sectionsInDeck = new HashSet<int>();

            for (int i = 0; i < deck.Count; i++)
            {
                SlideSO slide = deck.SlideAt(i);
                string where = $"slide {i + 1}/{deck.Count}";

                if (slide == null)
                {
                    findings.Add(new Finding(Severity.Error,
                        $"{where}: empty entry in the slide list — the deck will log a render failure here.", deck));
                    continue;
                }

                if (seen.TryGetValue(slide, out int firstAt))
                    findings.Add(new Finding(Severity.Warning,
                        $"{where}: '{slide.name}' also appears at position {firstAt + 1}. " +
                        "Intentional for a recap, a mistake otherwise.", slide));
                else seen[slide] = i;

                sectionsInDeck.Add(slide.SectionNumber);

                if (string.IsNullOrWhiteSpace(slide.Title))
                    findings.Add(new Finding(Severity.Error,
                        $"{where} ('{slide.name}'): no Title — this slide renders blank.", slide));

                if (slide.SectionNumber <= 0)
                    findings.Add(new Finding(Severity.Warning,
                        $"{where} ('{slide.name}'): section number {slide.SectionNumber} is not a " +
                        "positive number, so no number key can reach it.", slide));

                if (string.IsNullOrEmpty(deck.ResolveSectionName(slide)) && slide.SectionNumber > 0)
                    findings.Add(new Finding(Severity.Info,
                        $"{where} ('{slide.name}'): section {slide.SectionNumber} has no name, so the " +
                        "eyebrow will be hidden. Fine for a poster or closing slide.", slide));

                ValidateTextSlide(slide, where, findings);
            }

            // ---- number-key reachability ----
            for (int section = 1; section <= 9; section++)
            {
                // Read the declared name straight from the deck's table. Reading it via a slide would
                // make 'declared' impossible for an absent section, which quietly kills the first
                // check below.
                bool declared = !string.IsNullOrWhiteSpace(deck.SectionNameAt(section));
                bool present = deck.FirstSlideOfSection(section) >= 0;

                if (declared && !present)
                    findings.Add(new Finding(Severity.Warning,
                        $"Section {section} is named in the deck but no slide claims it — key {section} " +
                        "does nothing.", deck));
                if (!declared && present)
                    findings.Add(new Finding(Severity.Info,
                        $"Section {section} has slides but no name in the deck's section table.", deck));
            }

            foreach (int s in sectionsInDeck)
            {
                if (s > 9)
                    findings.Add(new Finding(Severity.Info,
                        $"Section {s} is beyond the 1–9 number keys, so it is only reachable by " +
                        "stepping there.", deck));
            }

            // ---- orphaned slide assets ----
            foreach (string guid in AssetDatabase.FindAssets("t:SlideSO"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<SlideSO>(path);
                if (asset != null && !seen.ContainsKey(asset))
                    findings.Add(new Finding(Severity.Info,
                        $"'{asset.name}' exists but is not in this deck.", asset));
            }

            return Report(deck, findings);
        }

        private static void ValidateTextSlide(SlideSO slide, string where, List<Finding> findings)
        {
            if (!(slide is TextSlideSO text)) return;

            switch (text.Layout)
            {
                case TextSlideLayout.TitleAndBody when string.IsNullOrWhiteSpace(text.Body):
                    findings.Add(new Finding(Severity.Warning,
                        $"{where} ('{slide.name}'): layout is TitleAndBody but Body is empty — it will " +
                        "render as a title-only slide.", slide));
                    break;

                case TextSlideLayout.Bullets when text.Bullets.Count == 0:
                case TextSlideLayout.TwoColumn when text.Bullets.Count == 0:
                    findings.Add(new Finding(Severity.Error,
                        $"{where} ('{slide.name}'): layout is {text.Layout} but there are no bullets.", slide));
                    break;

                case TextSlideLayout.TwoColumn when text.Bullets.Count < 4:
                    findings.Add(new Finding(Severity.Info,
                        $"{where} ('{slide.name}'): only {text.Bullets.Count} bullets in TwoColumn — " +
                        "the right column will look thin. Bullets may read better.", slide));
                    break;
            }

            for (int b = 0; b < text.Bullets.Count; b++)
            {
                if (string.IsNullOrWhiteSpace(text.Bullets[b]))
                    findings.Add(new Finding(Severity.Warning,
                        $"{where} ('{slide.name}'): bullet {b + 1} is empty — it draws a hairline with " +
                        "no text.", slide));
            }

            foreach (SlideReadout r in text.Readouts)
            {
                if (string.IsNullOrWhiteSpace(r.Value))
                    findings.Add(new Finding(Severity.Warning,
                        $"{where} ('{slide.name}'): readout '{r.Label}' has no value.", slide));
            }
        }

        private static string Report(PresentationDeckSO deck, List<Finding> findings)
        {
            int errors = 0, warnings = 0, infos = 0;
            foreach (Finding f in findings)
            {
                if (f.Level == Severity.Error) errors++;
                else if (f.Level == Severity.Warning) warnings++;
                else infos++;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== Deck validation: {deck.name} — {deck.Count} slides ===");
            sb.AppendLine($"  {errors} error(s), {warnings} warning(s), {infos} note(s)");
            if (findings.Count == 0) sb.AppendLine("  nothing to fix.");
            foreach (Finding f in findings)
                sb.AppendLine($"  [{f.Level.ToString().ToUpperInvariant()}] {f.Message}");

            // Individually logged so each line is clickable straight to the asset. Capped so a deck
            // with a systemic mistake does not bury the summary.
            int emitted = 0;
            foreach (Finding f in findings)
            {
                if (f.Level == Severity.Info) continue;
                if (emitted++ >= 25) break;
                string msg = "[DeckValidator] " + f.Message;
                if (f.Level == Severity.Error) Debug.LogError(msg, f.Context);
                else Debug.LogWarning(msg, f.Context);
            }

            return sb.ToString();
        }

        private static PresentationDeckSO Resolve()
        {
            if (Selection.activeObject is PresentationDeckSO selected) return selected;

            string[] guids = AssetDatabase.FindAssets("t:PresentationDeckSO");
            if (guids.Length == 0) return null;
            if (guids.Length > 1)
                Debug.LogWarning($"[DeckValidator] {guids.Length} decks exist; validating the first. " +
                                 "Select one to be explicit.");
            return AssetDatabase.LoadAssetAtPath<PresentationDeckSO>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
#endif
