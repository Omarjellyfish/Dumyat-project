using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>The drawn vocabulary. Every one is assembled from plain elements — see DiagramIcons.</summary>
    public enum DiagramIcon
    {
        None = 0,
        Microphone = 1,
        Waveform = 2,
        Chip = 3,
        SpeechBubble = 4,
        Speaker = 5,
        Subtitle = 6,
        Balance = 7,
        ConfirmGate = 8,
        Outcome = 9,
        Ledger = 10,
        Blocked = 11,
    }

    /// <summary>Which palette role a mark takes. Resolved against the deck theme at draw time.</summary>
    public enum DiagramTint
    {
        Ink = 0,
        Accent = 1,
        Jade = 2,
        Coral = 3,
        Muted = 4,
    }

    public enum DiagramNodeKind
    {
        /// <summary>A drawn symbol from <see cref="DiagramIcon"/>.</summary>
        Icon = 0,

        /// <summary>A rounded plate carrying a short string and a row of priority pips.</summary>
        Pill = 1,
    }

    /// <summary>
    /// One mark on the diagram.
    ///
    /// Position is NORMALISED (0–1 across the slide) rather than in pixels, so a diagram authored once
    /// lands correctly at Full HD, 2K and 4K alike — the same reason every other layout number in this
    /// deck is a reference pixel rather than a screen one.
    /// </summary>
    [System.Serializable]
    public struct DiagramNode
    {
        public DiagramNodeKind Kind;

        [Tooltip("Icon nodes only.")]
        public DiagramIcon Icon;

        [Tooltip("0–1 across the slide, from the top-left. 0.5,0.5 is the centre.")]
        public Vector2 Position;

        [Tooltip("Size in reference pixels — the icon's overall box, or the pill's width.")]
        public float Size;

        public DiagramTint Tint;

        [Tooltip("Pill nodes only. The one place a diagram slide carries words, for the times when the " +
                 "content genuinely is a name.")]
        public string Label;

        [Tooltip("Pill nodes only. Priority pips drawn to the right of the plate. 0 draws none.")]
        public int Pips;

        [Tooltip("Pill nodes only. A filled plate reads as the active choice, an outline as the fallback.")]
        public bool Filled;
    }

    /// <summary>A connector between two nodes, by index.</summary>
    [System.Serializable]
    public struct DiagramEdge
    {
        public int From;
        public int To;
        public DiagramTint Tint;

        [Tooltip("Dashed reads as conditional or absent; solid as the path actually taken.")]
        public bool Dashed;

        [Tooltip("Draw the arrowhead at the To end.")]
        public bool Arrow;

        [Tooltip("Strike this connector through, for a route that deliberately does NOT exist. The " +
                 "crossed-out arrow from the model to the money is the whole claim of the pipeline slide.")]
        public bool Struck;
    }

    /// <summary>
    /// A slide that is a drawing rather than prose — a mechanism the audience should see in one look
    /// instead of assembling from bullets.
    ///
    /// WHY A SLIDE TYPE RATHER THAN AN EXPORTED IMAGE:
    /// a PNG goes stale the moment a model or a stage changes, cannot be restyled when the deck's palette
    /// moves, and softens the moment the deck runs above the resolution it was exported at — which it now
    /// does, since the output resolution is a setting. Assembling the drawing from elements keeps it
    /// crisp at 4K and keeps its colours coming from the theme.
    ///
    /// UI Toolkit has no SVG and no clip-path, so every symbol here is built from rectangles, borders and
    /// border-radius. That is a real constraint on what can be drawn, and it is why the vocabulary is an
    /// enum of known symbols rather than arbitrary path data.
    /// </summary>
    [CreateAssetMenu(fileName = "Slide_Diagram", menuName = "Dumyat/Presentation/Diagram Slide")]
    public sealed class DiagramSlideSO : SlideSO
    {
        [Header("Drawing")]
        [SerializeField, Tooltip("Marks, in draw order. Edges reference these by index, so reordering " +
                                 "this list re-points the edges — add rather than reorder.")]
        private DiagramNode[] _nodes = new DiagramNode[0];

        [SerializeField, Tooltip("Connectors between nodes, drawn under them.")]
        private DiagramEdge[] _edges = new DiagramEdge[0];

        [Header("Framing")]
        [SerializeField, Tooltip("Show the slide's Title above the drawing. Off for a wholly wordless " +
                                 "slide, which is what the merchant AI pair is.")]
        private bool _showTitle;

        public DiagramNode[] Nodes => _nodes;
        public DiagramEdge[] Edges => _edges;
        public bool ShowTitle => _showTitle;

        /// <summary>The drawing supplies its own ground, so the deck backdrop stays.</summary>
        public override bool WantsBackdrop => true;
    }
}
