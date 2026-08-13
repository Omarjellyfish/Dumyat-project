using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// How the deck gets from one slide to the next. One asset per transition, shared deck-wide, so
    /// retuning the whole deck in rehearsal is editing one asset rather than touching any script.
    ///
    /// The invariant every kind must honour: the slide swap happens while the screen is fully
    /// covered, and never in the same frame as the keypress.
    /// </summary>
    [CreateAssetMenu(fileName = "Transition", menuName = "Dumyat/Presentation/Transition")]
    public sealed class SlideTransitionSO : ScriptableObject
    {
        [SerializeField] private SlideTransitionKind _kind = SlideTransitionKind.FadeToBlack;

        [SerializeField, Range(0f, 1.5f),
         Tooltip("Seconds to cover the screen. Halved for a cross-dissolve, which has no hold.")]
        private float _outDuration = 0.22f;

        [SerializeField, Range(0f, 1.5f), Tooltip("Seconds to uncover it again.")]
        private float _inDuration = 0.22f;

        [SerializeField, Range(0f, 1f),
         Tooltip("Seconds to hold fully covered, on top of the two durations. Gives a stage swap " +
                 "time to render a few frames before anyone sees it.")]
        private float _hold = 0f;

        [SerializeField, Tooltip("Colour of the covering layer. Black for a dip, near-white for a " +
                                 "section break, warm white for the sun whiteout.")]
        private Color _coverColor = Color.black;

        [SerializeField, Tooltip("Eases both halves. Leave as the default S-curve unless a specific " +
                                 "transition needs a snap.")]
        private AnimationCurve _ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public SlideTransitionKind Kind => _kind;
        public float OutDuration => _kind == SlideTransitionKind.Cut ? 0f : _outDuration;
        public float InDuration => _kind == SlideTransitionKind.Cut ? 0f : _inDuration;
        public float Hold => _kind == SlideTransitionKind.Cut ? 0f : _hold;
        public Color CoverColor => _coverColor;

        public float Evaluate(float t) => _ease?.Evaluate(t) ?? t;

        /// <summary>Total wall-clock cost, used by the director to lock out navigation.</summary>
        public float TotalDuration => OutDuration + Hold + InDuration;
    }

    public enum SlideTransitionKind
    {
        /// <summary>No cover at all. Correct between camera angles inside one stage.</summary>
        Cut = 0,

        /// <summary>Dip through an opaque colour. The deck default and the safe fallback.</summary>
        FadeToBlack = 1,

        /// <summary>Same mechanism, pale colour. Reserved for section breaks.</summary>
        FadeToWhite = 2,

        /// <summary>
        /// Fades the content layer rather than covering it, so the backdrop stays put. Text slide to
        /// text slide only — there is nothing underneath to protect.
        /// </summary>
        CrossDissolve = 3,
    }
}
