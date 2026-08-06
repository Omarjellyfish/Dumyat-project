using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Throws dust off the nail head on every blow, tinted so the destructive nail reads as
    /// destructive before the player has finished reading the word on it.
    ///
    /// Owns the PARTICLES only. The system is authored in the scene rather than built here, so its
    /// look stays art rather than becoming code.
    /// </summary>
    public class NailImpactDust : NailStrikeBehaviour
    {
        [Header("Particles")]
        [Tooltip("Burst system, moved to the nail head before each emit. One system per nail.")]
        [SerializeField] private ParticleSystem _particles;

        [SerializeField] private int _minBurst = 8;
        [SerializeField] private int _maxBurst = 14;

        [Header("Tint")]
        [Tooltip("Ordinary blows. Amber for a normal nail; coral (#FF9B8A) for Quit, matching the " +
                 "destructive colour the rest of the game's UI already uses.")]
        [SerializeField] private Color _tint = new Color(0.886f, 0.502f, 0.227f, 1f);   // #E2803A

        [Tooltip("The blow that sets the nail — brighter, so the last hit is visibly the last hit.")]
        [SerializeField] private Color _setTint = new Color(1f, 0.808f, 0.573f, 1f);    // #FFCE92

        [Tooltip("A blow that a locked nail refuses. Grey: nothing was driven, so nothing burns.")]
        [SerializeField] private Color _refusedTint = new Color(0.62f, 0.60f, 0.57f, 1f);

        protected override void Awake()
        {
            base.Awake();

            if (_particles == null)
                Debug.LogWarning($"{nameof(NailImpactDust)} on '{name}' has no ParticleSystem assigned — " +
                                 "blows will land without dust.", this);
        }

        protected override void HandleStruck(NailStrike strike) =>
            Burst(strike.Point, strike.IsFinal ? _setTint : _tint, 1f);

        protected override void HandleRefused() =>
            Burst(Nail != null ? Nail.transform.position : transform.position, _refusedTint, 0.4f);

        private void Burst(Vector3 point, Color colour, float scale)
        {
            if (_particles == null) return;

            _particles.transform.position = point;

            ParticleSystem.MainModule main = _particles.main;
            main.startColor = colour;

            int count = Mathf.Max(1, Mathf.RoundToInt(Random.Range(_minBurst, _maxBurst + 1) * scale));
            _particles.Emit(count);
        }
    }
}
