using UnityEngine;

namespace DumyatRuntime
{
    [RequireComponent(typeof(Rigidbody))]
    public class CollisionAudio : MonoBehaviour
    {
        [Header("Audio Settings")]
        [SerializeField] private AudioClip[] _impactSounds;
        [SerializeField] private float _minImpactVelocity = 1.0f;
        [SerializeField] private float _maxImpactVelocity = 10.0f;
        
        [Range(0f, 1f)]
        [SerializeField] private float _baseVolume = 1.0f;

        [Header("Performance")]
        [SerializeField] private float _cooldownTime = 0.1f;
        private float _lastPlayTime;

        private void OnCollisionEnter(Collision collision)
        {
            if (_impactSounds == null || _impactSounds.Length == 0) return;
            if (SoundManager.Singleton == null) return;
            if (Time.time < _lastPlayTime + _cooldownTime) return;

            float impactMagnitude = collision.relativeVelocity.magnitude;

            if (impactMagnitude >= _minImpactVelocity)
            {
                // Calculate volume based on impact strength
                float normalizedImpact = Mathf.InverseLerp(_minImpactVelocity, _maxImpactVelocity, impactMagnitude);
                float volume = Mathf.Lerp(0.2f, 1.0f, normalizedImpact) * _baseVolume;

                SoundManager.Singleton.PlayRandomSoundFxAtPosition(_impactSounds, transform, volume);
                
                _lastPlayTime = Time.time;
            }
        }
    }
}
