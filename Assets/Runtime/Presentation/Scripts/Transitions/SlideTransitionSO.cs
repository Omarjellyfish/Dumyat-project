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

        [Header("Push and wipe only")]
        [SerializeField, Tooltip("Which way the outgoing frame travels. Left is the natural 'forward' " +
                                 "for a deck that reads left to right.")]
        private TransitionDirection _direction = TransitionDirection.Left;

        [Header("Zoom-through only")]
        [SerializeField, Range(1.02f, 2f),
         Tooltip("How far the frozen frame punches toward the viewer before it is gone. 1.35 reads as " +
                 "flying in; past about 1.6 the still turns to mush before it finishes fading.")]
        private float _zoomScale = 1.35f;

        public SlideTransitionKind Kind => _kind;
        public float OutDuration => _kind == SlideTransitionKind.Cut ? 0f : _outDuration;
        public float InDuration => _kind == SlideTransitionKind.Cut ? 0f : _inDuration;
        public float Hold => _kind == SlideTransitionKind.Cut ? 0f : _hold;
        public Color CoverColor => _coverColor;
        public TransitionDirection Direction => _direction;
        public float ZoomScale => _zoomScale;

        /// <summary>
        /// True for the kinds that work by freezing the outgoing stage to a texture. They share one
        /// capture path, and — importantly — they never darken the screen, because the still itself is
        /// what covers the swap.
        /// </summary>
        public bool UsesFreeze =>
            _kind == SlideTransitionKind.StageDissolve ||
            _kind == SlideTransitionKind.StagePush ||
            _kind == SlideTransitionKind.StageZoomThrough ||
            _kind == SlideTransitionKind.StageWipe;

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

        /// <summary>
        /// Place to place: the outgoing frame freezes and dissolves away over the new one, drifting
        /// very slightly forward as it goes.
        ///
        /// This is a real cross-dissolve between two scenes, which should be impossible here — the deck
        /// only ever renders one stage, and lighting a second one to dissolve from would mean two live
        /// stages and two full renders on a 4GB card. The trick is that one side does not need to be
        /// live: the outgoing stage is captured to a single RenderTexture in the frame before the swap,
        /// so what dissolves is a still. Cost is one texture and one extra camera render, once.
        ///
        /// Never use this between two angles of ONE place. Cinemachine is already easing the camera
        /// there, and a dissolve would hide the very move it exists to show.
        /// </summary>
        StageDissolve = 4,

        /// <summary>
        /// The frozen outgoing frame slides bodily off screen, uncovering the new place behind it.
        /// Reads as travel — the strongest "we have gone somewhere else" of the set, which makes it the
        /// one to use when the audience needs to feel the distance between two places.
        /// </summary>
        StagePush = 5,

        /// <summary>
        /// The frozen frame punches toward the viewer and fades. Same still, far more aggressive scale
        /// than <see cref="StageDissolve"/>, so it reads as flying INTO the new place rather than easing
        /// across to it. Good for entering a section, tiring if used for every place change.
        /// </summary>
        StageZoomThrough = 6,

        /// <summary>
        /// A hard-edged wipe: the frozen frame is clipped away along one axis and the live place is
        /// revealed underneath by a travelling straight edge. No cross-fade at any point, so it stays
        /// crisp where a dissolve goes soft — the most editorial-looking option here.
        /// </summary>
        StageWipe = 7,

        /// <summary>
        /// Dip to a colour, name the place on the covered screen, then lift. The slowest transition in
        /// the deck and the only one that carries information: it doubles as a chapter marker, so the
        /// audience is told where they have arrived rather than having to read it off the lockup.
        /// Worth the extra second when moving to a genuinely new part of the talk.
        /// </summary>
        LocationSlate = 8,
    }

    /// <summary>Which way a push or wipe travels. Ignored by every other kind.</summary>
    public enum TransitionDirection
    {
        /// <summary>Outgoing frame exits left — the natural "forward" for a left-to-right deck.</summary>
        Left = 0,
        Right = 1,
        Up = 2,
        Down = 3,
    }
}
