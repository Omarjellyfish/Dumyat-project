using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Rotates one showcase model on its plinth.
    ///
    /// Deliberately the only script a demo stage is allowed to run. It uses unscaled time for the same
    /// reason the transitions do — the gameplay root container can put <see cref="Time.timeScale"/> at
    /// zero, and a turntable that silently stops is worse than no turntable.
    ///
    /// Named so it survives <c>DemoStripRulesSO</c>'s keep-list, which protects component names
    /// containing "Spinner" and "Rotator". The turntable scene is authored rather than copied, so it is
    /// never stripped — but a future re-strip should not be able to freeze it.
    /// </summary>
    public sealed class TurntableSpinner : MonoBehaviour
    {
        [SerializeField, Tooltip("Degrees per second. Slow enough to read the silhouette, fast enough " +
                                 "that a slide held for 20 seconds shows every side.")]
        private float _degreesPerSecond = 18f;

        [SerializeField, Tooltip("Stagger so a row of plinths does not turn in lockstep, which reads " +
                                 "as a machine rather than a display.")]
        private float _startOffsetDegrees;

        [SerializeField] private Vector3 _axis = Vector3.up;

        private void Start()
        {
            if (Mathf.Abs(_startOffsetDegrees) > 0.01f)
                transform.Rotate(_axis, _startOffsetDegrees, Space.Self);
        }

        private void Update()
        {
            transform.Rotate(_axis, _degreesPerSecond * Time.unscaledDeltaTime, Space.Self);
        }
    }
}
