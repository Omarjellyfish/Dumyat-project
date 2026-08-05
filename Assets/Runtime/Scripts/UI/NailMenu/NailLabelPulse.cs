using System.Collections;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The label's SCALE: a barely-there idle breath while the nail is live, and one overshoot on
    /// the blow that sets it.
    ///
    /// Both live here rather than in two components because they write the same property. Two
    /// separate scripts each assigning localScale would mean whichever ran second won, and the
    /// breath would silently eat the snap.
    /// </summary>
    public class NailLabelPulse : NailStrikeBehaviour
    {
        [Header("Pulse")]
        [Tooltip("What scales. This GameObject's transform when left empty.")]
        [SerializeField] private Transform _target;

        private Vector3 _rest;
        private float _snap;        // extra scale contributed by the set-snap
        private float _phase;       // per-nail offset so a row of nails does not breathe in unison
        private bool _driven;
        private Coroutine _running;

        protected override void Awake()
        {
            base.Awake();

            if (_target == null) _target = transform;
            _rest = _target.localScale;
            _phase = Random.value * 10f;
        }

        protected override void HandleRestored()
        {
            if (_running != null)
            {
                StopCoroutine(_running);
                _running = null;
            }

            _driven = false;
            _snap = 0f;

            if (_target != null) _target.localScale = _rest;
        }

        protected override void HandleSet(NailStrike strike)
        {
            _driven = true;   // the breath stops: this nail is finished, not waiting

            if (_target == null || Style == null || !isActiveAndEnabled) return;

            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(Snap());
        }

        // One writer, so the breath and the snap add rather than overwrite.
        private void Update()
        {
            if (_target == null || Style == null) return;

            float breath = 0f;

            if (!_driven && Style.BreathAmplitude > 0f)
            {
                float turns = (Time.unscaledTime + _phase) * (Mathf.PI * 2f) / Style.BreathSeconds;
                breath = Mathf.Sin(turns) * Style.BreathAmplitude;
            }

            _target.localScale = _rest * (1f + breath + _snap);
        }

        private IEnumerator Snap()
        {
            float duration = Style.SetSeconds;
            float peak = Style.SetOvershoot;

            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                // Front-loaded hump: up fast on the impact, down slower as it settles. A plain
                // sine rises too politely for something that has just been hammered flat.
                _snap = peak * Mathf.Sin(Mathf.Pow(Mathf.Clamp01(t / duration), 0.6f) * Mathf.PI);
                yield return null;
            }

            _snap = 0f;
            _running = null;
        }
    }
}
