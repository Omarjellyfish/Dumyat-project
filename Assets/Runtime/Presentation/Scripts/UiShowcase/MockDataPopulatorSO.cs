using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Fills a cloned UI panel with believable content for the showcase.
    ///
    /// WHY THE PRESENTERS ARE NOT REUSED:
    /// every real panel presenter wants the gameplay stack — <c>IInventoryService</c>, a live save file,
    /// a quest database, an XR rig. Standing all that up just to screenshot a panel would drag the
    /// entire game into the deck, and the demo scenes have had all of it stripped out. Writing into the
    /// visual tree directly keeps the panels REAL (the shipping UXML and USS, so they cannot go stale)
    /// while needing nothing behind them.
    ///
    /// Subclass this when a panel needs computed content. <see cref="PanelMockDataSO"/> covers the
    /// common case — named labels and cloned rows — as pure data.
    /// </summary>
    public abstract class MockDataPopulatorSO : ScriptableObject
    {
        /// <summary>Writes mock content into a freshly cloned panel tree.</summary>
        public abstract void Apply(VisualElement root);

        /// <summary>
        /// Sets a Label's text if it exists, and reports if it does not — a renamed element in the
        /// shipping UXML should surface as a warning here rather than as a blank panel on a projector.
        /// </summary>
        protected static bool SetLabel(VisualElement root, string elementName, string text)
        {
            if (root == null || string.IsNullOrEmpty(elementName)) return false;

            var label = root.Q<Label>(elementName);
            if (label != null) { label.text = text; return true; }

            // Buttons are Labels' cousins, not Labels — panels use both for captions.
            var button = root.Q<Button>(elementName);
            if (button != null) { button.text = text; return true; }

            Debug.LogWarning($"[MockData] no Label or Button named '{elementName}' in this panel — " +
                             "the shipping UXML may have been renamed.");
            return false;
        }
    }
}
