using System;
using UnityEngine;

namespace DumyatRuntime
{
    // Base class for a hittable nail. Handles hit counting, driving the nail visual in, and
    // playing a hit sound. Subclasses just say what happens once the nail is fully driven
    // (see NailMenuAction).
    //
    // It also RAISES rather than performs its own presentation. Everything the player sees beyond
    // the nail moving — the label heating up, the letters taking the shock, the dust — lives in
    // separate NailStrikeBehaviour components that subscribe to the events below. Keeping them
    // apart means a designer composes a nail's feel by adding components in the Inspector, and a
    // nail with no reactions attached still works exactly as it did before.
    public abstract class NailAction : MonoBehaviour
    {
        [Header("Nail")]
        [SerializeField] private Transform _nailVisual;
        [SerializeField] private int _hitsRequired = 1;
        [SerializeField] private float _driveDistancePerHit = 0.01f;
        [SerializeField] private float _minHitSpeed = 0.5f;

        [Header("Presentation")]
        [Tooltip("Look and timing preset shared by every reaction component on this nail. Each one " +
                 "can override it, but leaving them empty means the whole nail is retuned from here.")]
        [SerializeField] private NailLabelStyleSO _labelStyle;

        [Header("Feedback")]
        [SerializeField] private AudioSource _hitAudioSource;
        [SerializeField] private AudioClip _hitClip;

        [Tooltip("How far the hit clip climbs in pitch between the first blow and the last. The rising " +
                 "tone is what tells the player the nail is nearly set without them looking at it.")]
        [Range(0f, 1f)]
        [SerializeField] private float _pitchClimb = 0.35f;

        [Tooltip("Pitch of the dull knock played when the hammer lands on a nail that cannot be " +
                 "driven — Continue with no save. Below 1 so it reads as a refusal, not a hit.")]
        [Range(0.4f, 1f)]
        [SerializeField] private float _refusedPitch = 0.72f;

        /// <summary>Raised for every blow that lands and counts, including the one that sets the nail.</summary>
        public event Action<NailStrike> Struck;

        /// <summary>Raised once, on the blow that drives the nail home, just before the action fires.</summary>
        public event Action<NailStrike> Set;

        /// <summary>Raised by <see cref="ResetNail"/> — the nail is back out and hittable from scratch.</summary>
        public event Action Restored;

        /// <summary>Raised when a blow lands on a nail that <see cref="IsAvailable"/> refuses.</summary>
        public event Action Refused;

        public int HitsRequired => Mathf.Max(1, _hitsRequired);
        public int CurrentHits => _currentHits;
        public bool IsDriven => _isDriven;
        public NailLabelStyleSO LabelStyle => _labelStyle;

        /// <summary>
        /// False when the nail is present but must not be driven — Continue with no save to resume.
        ///
        /// Read fresh on every blow rather than cached, because the answer can change while the menu
        /// is open: the same nail is un-hittable on a first launch and hittable the moment a save
        /// exists, and nothing tells this component when that happened.
        /// </summary>
        public virtual bool IsAvailable => true;

        private Vector3 _nailVisualStartLocalPosition;
        private int _currentHits;
        private bool _isDriven;

        private void Awake()
        {
            if (_nailVisual != null)
                _nailVisualStartLocalPosition = _nailVisual.localPosition;
        }

        // Reactions attach in their own OnEnable, which can run before or after this component's.
        // Announcing the rest state in Start — after every OnEnable in the scene has run — is what
        // guarantees a label starts cold rather than at whatever the prefab was authored with.
        private void Start() => Restored?.Invoke();

        // Called by HammerHead when it collides with this nail.
        public void Hit(float impactSpeed)
        {
            if (_isDriven) return;
            if (impactSpeed < _minHitSpeed) return;

            if (!IsAvailable)
            {
                // Answer the blow rather than swallowing it. Silence is indistinguishable from a
                // broken nail; a dull knock reads as "not this one".
                PlayHitSound(0f, _refusedPitch);
                Refused?.Invoke();
                return;
            }

            _currentHits++;
            DriveNailVisual();

            NailStrike strike = new NailStrike(
                _currentHits,
                HitsRequired,
                impactSpeed,
                _nailVisual != null ? _nailVisual.position : transform.position);

            PlayHitSound(strike.Progress, 1f);
            Struck?.Invoke(strike);

            if (_currentHits >= HitsRequired)
            {
                _isDriven = true;
                Set?.Invoke(strike);
                OnFullyDriven();
            }
        }

        // Pops the nail visual back to its starting position and clears the hit count, so it can be
        // hit again from scratch. Call this whenever the nail is hidden/shown again (see
        // NailGroupController).
        public void ResetNail()
        {
            _currentHits = 0;
            _isDriven = false;

            if (_nailVisual != null)
                _nailVisual.localPosition = _nailVisualStartLocalPosition;

            if (_hitAudioSource != null)
                _hitAudioSource.pitch = 1f;

            Restored?.Invoke();
        }

        private void DriveNailVisual()
        {
            if (_nailVisual != null)
                _nailVisual.Translate(Vector3.down * _driveDistancePerHit, Space.Self);
        }

        /// <summary>
        /// PlayOneShot takes its pitch from the source, so the source is retuned first. Blows climb
        /// toward the set point; a refused blow uses <paramref name="pitchOverride"/> instead.
        /// </summary>
        private void PlayHitSound(float progress, float pitchOverride)
        {
            if (_hitAudioSource == null || _hitClip == null) return;

            _hitAudioSource.pitch = pitchOverride < 1f ? pitchOverride : 1f + _pitchClimb * progress;
            _hitAudioSource.PlayOneShot(_hitClip);
        }

        protected abstract void OnFullyDriven();
    }
}
