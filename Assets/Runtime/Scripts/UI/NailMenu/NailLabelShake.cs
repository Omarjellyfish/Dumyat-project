using System.Collections;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Kicks the label when the nail is struck, so the word visibly takes the same blow the nail did.
    ///
    /// Owns the label's LOCAL POSITION only. The kick runs along the nail's own axis rather than
    /// sideways on purpose: a lateral shake reads as a rattle, while a downward one reads as the
    /// force that just moved the nail.
    /// </summary>
    public class NailLabelShake : NailStrikeBehaviour
    {
        [Header("Shake")]
        [Tooltip("What moves. This GameObject's transform when left empty.")]
        [SerializeField] private Transform _target;

        [Tooltip("Direction of the kick in the target's LOCAL space — the nail's driving axis.")]
        [SerializeField] private Vector3 _axis = Vector3.down;

        [Tooltip("Fraction of the normal kick used when a nail refuses the blow. A flinch rather " +
                 "than a hit, so a locked nail feels solid instead of unresponsive.")]
        [Range(0f, 1f)]
        [SerializeField] private float _refusedScale = 0.35f;

        private Vector3 _rest;
        private Coroutine _running;

        protected override void Awake()
        {
            base.Awake();

            if (_target == null) _target = transform;
            _rest = _target.localPosition;
        }

        protected override void HandleStruck(NailStrike strike) => Kick(1f);

        protected override void HandleRefused() => Kick(_refusedScale);

        protected override void HandleRestored()
        {
            if (_running != null)
            {
                StopCoroutine(_running);
                _running = null;
            }

            if (_target != null) _target.localPosition = _rest;
        }

        private void Kick(float scale)
        {
            if (_target == null || Style == null || !isActiveAndEnabled) return;

            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(Shake(scale));
        }

        private IEnumerator Shake(float scale)
        {
            float duration = Style.ShakeSeconds;
            float distance = Style.ShakeDistance * scale;
            Vector3 direction = _axis.sqrMagnitude > 0f ? _axis.normalized : Vector3.down;

            // Unscaled: the menu must feel the same whether or not something has frozen time.
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float amount = Style.ShakeCurve != null ? Style.ShakeCurve.Evaluate(t / duration) : 0f;
                _target.localPosition = _rest + direction * (distance * amount);
                yield return null;
            }

            _target.localPosition = _rest;
            _running = null;
        }
    }
}
