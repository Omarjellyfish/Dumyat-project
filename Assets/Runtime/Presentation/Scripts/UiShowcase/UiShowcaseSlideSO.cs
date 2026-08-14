using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// A slide showing real UI panels: either all of them at once as a grid, or one of them large.
    /// The same <see cref="UiPanelEntrySO"/> assets feed both modes.
    /// </summary>
    [CreateAssetMenu(fileName = "Slide_Ui", menuName = "Dumyat/Presentation/UI Showcase Slide")]
    public sealed class UiShowcaseSlideSO : SlideSO
    {
        [Header("Mode")]
        [SerializeField] private UiShowcaseMode _mode = UiShowcaseMode.Focus;

        [Header("Panels")]
        [SerializeField, Tooltip("Grid mode shows all of these. Focus mode shows the first one.")]
        private List<UiPanelEntrySO> _panels = new List<UiPanelEntrySO>();

        [Header("Grid layout")]
        [SerializeField, Range(2, 6), Tooltip("Tiles per row in Grid mode.")]
        private int _columns = 4;

        public UiShowcaseMode Mode => _mode;
        public IReadOnlyList<UiPanelEntrySO> Panels => _panels;
        public int Columns => _columns;

        /// <summary>The single panel a Focus slide is about, or null.</summary>
        public UiPanelEntrySO FocusPanel => _panels.Count > 0 ? _panels[0] : null;
    }

    public enum UiShowcaseMode
    {
        /// <summary>Every panel as a tile — the "here is the whole interface" slide.</summary>
        Grid = 0,

        /// <summary>One panel large, with its name and caption.</summary>
        Focus = 1,
    }
}
