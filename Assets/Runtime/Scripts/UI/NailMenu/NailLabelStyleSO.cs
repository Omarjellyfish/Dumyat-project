using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// How a nail's label looks and moves when it is struck — the "Brand" treatment by default: a
    /// maker's mark scorched into the bench that heats as the nail goes in.
    ///
    /// HOW THE HEAT IS SHOWN, AND WHY IT IS SPLIT IN TWO
    /// ------------------------------------------------
    /// The obvious version — lerp the letter fill from char to hot iron — does not survive contact
    /// with a wooden bench. Somewhere in the middle of that lerp the fill passes straight through
    /// the plank's own mid-brown, and a half-driven label disappears into the table.
    ///
    /// So the fill and the ember are separate ramps. The FILL stays dark for most of the drive (a
    /// heavily eased curve) and only flares at the very end, which keeps dark-on-wood contrast the
    /// whole way. The EMBER RIM around the letters carries the progress instead, growing steadily
    /// from nothing — it reads as the burn edge glowing, and it never competes with the plank
    /// because it is brighter than anything else on the bench.
    ///
    /// A constant dark shadow underneath does the rest: it separates the mark from the grain at
    /// every stage, including before the first blow.
    ///
    /// Everything here is data so the whole menu is retuned by editing one asset, and so a different
    /// direction (a Delta glass tag, chalk on the plank) is a second asset rather than a second set
    /// of components. The reaction components read it; none of them own a colour or a duration.
    /// </summary>
    [CreateAssetMenu(fileName = "NailLabelStyle", menuName = "Dumyat/Menus/Nail Label Style")]
    public class NailLabelStyleSO : ScriptableObject
    {
        [Header("Fill — stays dark so it never matches the plank")]
        [Tooltip("Letter fill before the first blow. Near-black: it has to read as a burn against " +
                 "mid-brown wood.")]
        [SerializeField] private Color _restColor = new Color(0.118f, 0.071f, 0.024f, 1f);      // #1E1206

        [Tooltip("Fill once the nail is flush — the hot core of the burn.")]
        [SerializeField] private Color _drivenColor = new Color(1f, 0.890f, 0.690f, 1f);        // #FFE3B0

        [Tooltip("Deliberately slow to start. The fill must stay darker than the bench for most of " +
                 "the drive, or a half-driven label vanishes into the table — flatten this and that " +
                 "is exactly what happens.")]
        [SerializeField] private AnimationCurve _fillCurve = LateFlare();

        [Tooltip("Seconds the look takes to catch up after a blow. Snappy, but not instant — an " +
                 "instant jump loses the sense of metal heating.")]
        [SerializeField] private float _driveEaseSeconds = 0.18f;

        [Header("Ember rim — this is what actually shows progress")]
        [Tooltip("Colour of the glowing burn edge. Brighter than anything else on the bench, so it " +
                 "reads at any drive depth.")]
        [SerializeField] private Color _emberColor = new Color(1f, 0.702f, 0.278f, 1f);         // #FFB347

        [Tooltip("Rim width before the first blow. Zero — an untouched mark is cold.")]
        [Range(0f, 1f)]
        [SerializeField] private float _restRim;

        [Tooltip("Rim width once the nail is flush.")]
        [Range(0f, 1f)]
        [SerializeField] private float _drivenRim = 0.22f;

        [Tooltip("Near-linear on purpose: this ramp is the player's read on how far in the nail is, " +
                 "so every blow should move it by a visible, equal amount.")]
        [SerializeField] private AnimationCurve _emberCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Shadow — constant, so the mark reads on any grain")]
        [SerializeField] private Color _shadowColor = new Color(0f, 0f, 0f, 0.85f);

        [Tooltip("Offset in TextMeshPro underlay units. Small: this is a mark pressed into wood, " +
                 "not a card floating above it.")]
        [SerializeField] private Vector2 _shadowOffset = new Vector2(0.6f, -0.6f);

        [Range(0f, 1f)]
        [SerializeField] private float _shadowDilate = 0.15f;

        [Range(0f, 1f)]
        [SerializeField] private float _shadowSoftness = 0.25f;

        [Header("Unavailable — a nail that refuses the hammer")]
        [Tooltip("Continue with no save. Dimmed rather than hidden, so the player can see the option " +
                 "exists and understand why it is not open to them.")]
        [Range(0f, 1f)]
        [SerializeField] private float _unavailableOpacity = 0.25f;

        [Header("Strike shake — the label takes the same blow the nail did")]
        [Tooltip("How far the label kicks, in the label's own local units. On a world-space Canvas " +
                 "at 0.001 scale this is millimetres.")]
        [SerializeField] private float _shakeDistance = 6f;

        [SerializeField] private float _shakeSeconds = 0.2f;

        [Tooltip("Down hard, overshoot back, settle. Evaluated 0..1 over the duration and multiplied " +
                 "by the distance.")]
        [SerializeField] private AnimationCurve _shakeCurve = Kick();

        [Header("Letter jolt — the shock travels through the word")]
        [Tooltip("Per-character displacement, in the text's local units.")]
        [SerializeField] private float _joltAmplitude = 5f;

        [Tooltip("Degrees each character twists at the peak of its jolt.")]
        [SerializeField] private float _joltRotationDegrees = 2.5f;

        [SerializeField] private float _joltSeconds = 0.19f;

        [Tooltip("Delay added per character, which is what makes the shock visibly travel outward " +
                 "from the nail instead of the whole word moving at once. Zero makes it a shudder.")]
        [SerializeField] private float _joltPerCharacterDelay = 0.012f;

        [SerializeField] private AnimationCurve _joltCurve = Kick();

        [Header("Set snap — the blow that drives it home")]
        [Tooltip("Extra scale at the peak of the snap. 0.08 = a 108% overshoot.")]
        [SerializeField] private float _setOvershoot = 0.08f;

        [SerializeField] private float _setSeconds = 0.26f;

        [Header("Idle breath")]
        [Tooltip("Scale wobble on a live, un-driven nail. Small enough not to be noticed directly; " +
                 "enough to stop the bench reading as a screenshot. Zero switches it off.")]
        [SerializeField] private float _breathAmplitude = 0.004f;

        [SerializeField] private float _breathSeconds = 2.4f;

        public Color RestColor => _restColor;
        public Color DrivenColor => _drivenColor;
        public AnimationCurve FillCurve => _fillCurve;
        public float DriveEaseSeconds => Mathf.Max(0f, _driveEaseSeconds);

        public Color EmberColor => _emberColor;
        public float RestRim => _restRim;
        public float DrivenRim => _drivenRim;
        public AnimationCurve EmberCurve => _emberCurve;

        public Color ShadowColor => _shadowColor;
        public Vector2 ShadowOffset => _shadowOffset;
        public float ShadowDilate => _shadowDilate;
        public float ShadowSoftness => _shadowSoftness;

        public float UnavailableOpacity => _unavailableOpacity;

        public float ShakeDistance => _shakeDistance;
        public float ShakeSeconds => Mathf.Max(0.01f, _shakeSeconds);
        public AnimationCurve ShakeCurve => _shakeCurve;

        public float JoltAmplitude => _joltAmplitude;
        public float JoltRotationDegrees => _joltRotationDegrees;
        public float JoltSeconds => Mathf.Max(0.01f, _joltSeconds);
        public float JoltPerCharacterDelay => Mathf.Max(0f, _joltPerCharacterDelay);
        public AnimationCurve JoltCurve => _joltCurve;

        public float SetOvershoot => _setOvershoot;
        public float SetSeconds => Mathf.Max(0.01f, _setSeconds);

        public float BreathAmplitude => _breathAmplitude;
        public float BreathSeconds => Mathf.Max(0.01f, _breathSeconds);

        /// <summary>
        /// Flat and low until the last third, then a hard rise. Keyed rather than eased so the
        /// "stays dark" part is explicit: at two thirds driven this is still only ~0.25.
        /// </summary>
        private static AnimationCurve LateFlare()
        {
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.5f, 0.10f),
                new Keyframe(0.75f, 0.30f),
                new Keyframe(1f, 1f));

            curve.SmoothTangents(1, 0.4f);
            curve.SmoothTangents(2, 0.4f);
            return curve;
        }

        /// <summary>
        /// The impact shape both the shake and the jolt default to: hard displacement, a smaller
        /// bounce past the rest position, then settle. Written once here because a hand-drawn curve
        /// that only approximates this is the difference between "hit" and "wobble".
        /// </summary>
        private static AnimationCurve Kick() => new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.22f, 1f),
            new Keyframe(0.55f, -0.25f),
            new Keyframe(1f, 0f));
    }
}
