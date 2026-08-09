using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Turns a step's <see cref="TutorialStepSO.HighlightIds"/> into the anchors that should actually
    /// light up.
    ///
    /// Extracted because <see cref="TutorialRunner"/> and <see cref="TutorialHighlighter"/> both walk
    /// that list, both de-duplicate it, and both warn about an id no anchor answers to — three copies
    /// of the same answer, drawn on the same frame, which is exactly the situation the "never write the
    /// same logic in two scripts" rule is about. They now ask here and differ only in what they DO with
    /// the result: the runner raises each anchor's own <c>OnHighlightChanged</c> hook and spawns custom
    /// art, the highlighter draws the built-in beacon or frame.
    /// </summary>
    public static class TutorialHighlightTargets
    {
        private static readonly HashSet<string> _ids = new HashSet<string>();

        /// <summary>
        /// Fills <paramref name="into"/> with every anchor this step should highlight, cleared first.
        ///
        /// <paramref name="context"/> is passed through to any warning so the Console entry selects
        /// the offending asset or component when clicked.
        ///
        /// <paramref name="warnOnMissing"/> exists because the runner and the highlighter both resolve
        /// the same step on the same frame; only the runner reports, so a step naming an absent anchor
        /// produces one Console line rather than two identical ones.
        /// </summary>
        public static void Collect(TutorialStepSO step, List<TutorialAnchor> into, Object context,
                                   bool warnOnMissing = true)
        {
            if (into == null) return;
            into.Clear();

            // PrimaryAnchorId is deliberately NOT a highlight target — it places the instruction CARD,
            // and is routinely 'Hands' or 'Player' on steps that describe a button rather than an
            // object. Only what the author listed in HighlightIds is highlighted, so a step that names
            // nothing correctly shows nothing.
            if (step == null || step.HighlightIds == null) return;

            // An id repeated between HighlightIds entries would otherwise stack two highlights on one
            // object, which reads as "why is that one glowing harder" rather than as a bug.
            _ids.Clear();
            foreach (string id in step.HighlightIds)
                if (!string.IsNullOrEmpty(id)) _ids.Add(id);

            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            foreach (string id in _ids)
            {
                if (step.HighlightNearestOnly)
                {
                    TutorialAnchor nearest = TutorialAnchorRegistry.GetNearestAnchor(id);
                    if (nearest == null)
                    {
                        if (warnOnMissing) WarnMissing(step, id, sceneName, context);
                        continue;
                    }

                    if (!into.Contains(nearest)) into.Add(nearest);
                    continue;
                }

                List<TutorialAnchor> anchors = TutorialAnchorRegistry.GetAllAnchors(id);
                if (anchors == null || anchors.Count == 0)
                {
                    if (warnOnMissing) WarnMissing(step, id, sceneName, context);
                    continue;
                }

                // Backwards: the registry hands back its LIVE list, so a destroyed anchor that has not
                // yet unregistered would otherwise take the loop with it.
                for (int i = anchors.Count - 1; i >= 0; i--)
                {
                    TutorialAnchor anchor = anchors[i];
                    if (anchor == null || into.Contains(anchor)) continue;
                    into.Add(anchor);
                }
            }
        }

        // Named but absent is an authoring mistake, not a design choice — say which step, and which
        // scene, because the usual cause is a step written for one scene running in another.
        private static void WarnMissing(TutorialStepSO step, string id, string sceneName, Object context)
        {
            Debug.LogWarning($"[Tutorial] Step '{step.name}' asks to highlight '{id}', but no " +
                             $"TutorialAnchor with that id is active in scene '{sceneName}'.",
                             context != null ? context : step);
        }
    }
}
