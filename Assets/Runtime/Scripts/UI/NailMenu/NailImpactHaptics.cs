using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Buzzes the controller on each blow, harder on the one that sets the nail.
    ///
    /// The rising amplitude is doing the same job as the rising pitch on the hit clip: telling the
    /// player how close the nail is to being driven without asking them to look at it.
    /// </summary>
    public class NailImpactHaptics : NailStrikeBehaviour
    {
        [Header("Hand")]
        [Tooltip("Which controller feels the blow. Both, unless the hammer is locked to one hand.")]
        [SerializeField] private HapticHand _hand = HapticHand.Both;

        [Header("Strength")]
        [Tooltip("Amplitude on the first blow. Climbs toward the set amplitude as the nail goes in.")]
        [Range(0f, 1f)]
        [SerializeField] private float _firstBlowAmplitude = 0.35f;

        [Range(0f, 1f)]
        [SerializeField] private float _setAmplitude = 0.85f;

        [SerializeField] private float _durationSeconds = 0.06f;
        [SerializeField] private float _frequency = 120f;

        [Tooltip("A blow a locked nail refuses — a short, weak tick rather than a hit.")]
        [Range(0f, 1f)]
        [SerializeField] private float _refusedAmplitude = 0.15f;

        private IHapticService _haptics;

        [Inject]
        private void Inject(IHapticService haptics) => _haptics = haptics;

        protected override void HandleStruck(NailStrike strike)
        {
            // Interpolating on Progress rather than switching on IsFinal means a five-blow Quit nail
            // ramps across all five, instead of four identical taps and one thump.
            float amplitude = Mathf.Lerp(_firstBlowAmplitude, _setAmplitude, strike.Progress);
            Pulse(amplitude, _durationSeconds);
        }

        protected override void HandleRefused() => Pulse(_refusedAmplitude, _durationSeconds * 0.5f);

        private void Pulse(float amplitude, float duration)
        {
            // Absent injection is normal in a scene with no ContainerScope — degrade quietly rather
            // than throwing on every hammer blow.
            _haptics?.TriggerVibration(amplitude, duration, _frequency, _hand);
        }
    }
}
