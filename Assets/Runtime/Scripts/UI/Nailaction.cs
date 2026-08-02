using UnityEngine;

namespace DumyatRuntime
{
    // Base class for a hittable nail. Handles hit counting, driving the
    // nail visual in, and playing a hit sound. Subclasses just say what
    // happens once the nail is fully driven in (see NailMenuAction).
    public abstract class NailAction : MonoBehaviour
    {
        [Header("Nail")]
        [SerializeField] private Transform _nailVisual;
        [SerializeField] private int _hitsRequired = 1;
        [SerializeField] private float _driveDistancePerHit = 0.01f;
        [SerializeField] private float _minHitSpeed = 0.5f;

        [Header("Feedback")]
        [SerializeField] private AudioSource _hitAudioSource;
        [SerializeField] private AudioClip _hitClip;

        private Vector3 _nailVisualStartLocalPosition;
        private int _currentHits;
        private bool _isDriven;

        private void Awake()
        {
            if (_nailVisual != null)
                _nailVisualStartLocalPosition = _nailVisual.localPosition;
        }

        // Called by HammerHead when it collides with this nail.
        public void Hit(float impactSpeed)
        {
            if (_isDriven) return;
            if (impactSpeed < _minHitSpeed) return;

            _currentHits++;
            DriveNailVisual();
            PlayHitSound();

            if (_currentHits >= _hitsRequired)
            {
                _isDriven = true;
                OnFullyDriven();
            }
        }

        // Pops the nail visual back to its starting position and clears
        // the hit count, so it can be hit again from scratch. Call this
        // whenever the nail is hidden/shown again (see NailGroupController).
        public void ResetNail()
        {
            _currentHits = 0;
            _isDriven = false;

            if (_nailVisual != null)
                _nailVisual.localPosition = _nailVisualStartLocalPosition;
        }

        private void DriveNailVisual()
        {
            if (_nailVisual != null)
                _nailVisual.Translate(Vector3.down * _driveDistancePerHit, Space.Self);
        }

        private void PlayHitSound()
        {
            if (_hitAudioSource != null && _hitClip != null)
                _hitAudioSource.PlayOneShot(_hitClip);
        }

        protected abstract void OnFullyDriven();
    }
}