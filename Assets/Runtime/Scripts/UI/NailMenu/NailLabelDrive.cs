using TMPro;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Heats the label as the nail goes in: a cold scorch at rest, a glowing burn once it is flush.
    ///
    /// Owns the label's COLOUR and nothing else — position, scale and per-character motion belong to
    /// <see cref="NailLabelShake"/>, <see cref="NailLabelPulse"/> and <see cref="NailLabelJolt"/>.
    /// Splitting them by the channel each one writes is what keeps two effects from quietly
    /// overwriting each other every frame.
    ///
    /// It writes three of TextMeshPro's channels, each doing a different job (see
    /// <see cref="NailLabelStyleSO"/> for why the heat is split rather than being one lerp):
    ///   • vertex colour → the fill, which stays dark so it never matches the plank
    ///   • outline       → the ember rim, which carries the drive progress
    ///   • underlay      → a constant shadow, so the mark reads on any grain
    ///
    /// The last two go through <see cref="TmpEngraving"/>, shared with the carved credits — see
    /// there for why this is built on outline and underlay rather than TextMeshPro's Glow.
    /// </summary>
    public class NailLabelDrive : NailStrikeBehaviour
    {
        [Header("Label")]
        [Tooltip("Text to heat. Taken from this GameObject when left empty.")]
        [SerializeField] private TMP_Text _label;

        private float _target;      // where the drive should be
        private float _shown;       // where it currently is, easing toward the target
        private bool _available = true;

        // The instanced font material, so one nail heating up does not heat every other label that
        // happens to share the font asset.
        private Material _material;
        private bool _hasOutline;
        private bool _hasUnderlay;

        protected override void Awake()
        {
            base.Awake();

            if (_label == null) _label = GetComponent<TMP_Text>();
            if (_label == null)
            {
                Debug.LogWarning($"{nameof(NailLabelDrive)} on '{name}' has no {nameof(TMP_Text)} — " +
                                 "the label will not react to the hammer.", this);
                return;
            }

            // fontMaterial (not sharedMaterial) is what forks a per-label instance. Reading it here
            // rather than per-frame keeps the fork to one allocation.
            _material = _label.fontMaterial;
            _hasOutline = TmpEngraving.HasOutline(_material);
            _hasUnderlay = TmpEngraving.HasUnderlay(_material);
        }

        protected override void HandleRestored()
        {
            _target = 0f;
            _shown = 0f;
            _available = Nail == null || Nail.IsAvailable;
            Apply(0f);
        }

        protected override void HandleStruck(NailStrike strike) => _target = strike.Progress;

        private void Update()
        {
            if (Mathf.Approximately(_shown, _target)) return;

            float ease = Style != null ? Style.DriveEaseSeconds : 0f;

            _shown = ease <= 0f
                ? _target
                : Mathf.MoveTowards(_shown, _target, Time.unscaledDeltaTime / ease);

            Apply(_shown);
        }

        private void Apply(float drive)
        {
            if (_label == null || Style == null) return;

            // Dimmed, not hidden: the player should see that Continue exists and infer why it is
            // shut, rather than wonder where the option went.
            float dim = _available ? 1f : Style.UnavailableOpacity;

            float fill = Style.FillCurve != null ? Style.FillCurve.Evaluate(drive) : drive;
            Color colour = Color.Lerp(Style.RestColor, Style.DrivenColor, fill);
            colour.a *= dim;
            _label.color = colour;

            if (_material == null) return;

            if (_hasOutline)
            {
                float ember = Style.EmberCurve != null ? Style.EmberCurve.Evaluate(drive) : drive;

                Color rim = Style.EmberColor;
                rim.a *= dim;

                TmpEngraving.TrySetOutline(_material, rim, Mathf.Lerp(Style.RestRim, Style.DrivenRim, ember));
            }

            if (_hasUnderlay)
            {
                Color shadow = Style.ShadowColor;
                shadow.a *= dim;

                TmpEngraving.TrySetUnderlay(_material, shadow, Style.ShadowOffset,
                                            Style.ShadowDilate, Style.ShadowSoftness);
            }

            TmpEngraving.Repad(_label);
        }
    }
}
