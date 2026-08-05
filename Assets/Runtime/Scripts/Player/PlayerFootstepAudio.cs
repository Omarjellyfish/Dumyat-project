using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// Tracks the player's head movement (Camera.main) along the XZ plane and triggers footstep sounds
    /// at the base of the player's rig. This works for both thumbstick locomotion and physical room-scale walking.
    /// </summary>
    public class PlayerFootstepAudio : MonoBehaviour
    {
        [Header("Footsteps")]
        [Tooltip("Clips to pick from randomly for footsteps.")]
        [SerializeField] private AudioClip[] _footstepClips;

        [Tooltip("Distance in meters the player must move horizontally to trigger a footstep.")]
        [SerializeField] private float _stepDistance = 1.2f;

        [Tooltip("Volume of the footsteps.")]
        [Range(0f, 1f)]
        [SerializeField] private float _volume = 0.5f;

        private ISoundService _soundService;
        private Transform _headTransform;
        private Vector3 _lastPosition;
        private float _distanceMoved;

        [Inject]
        private void Inject(ISoundService soundService)
        {
            _soundService = soundService;
        }

        private void Start()
        {
            if (Camera.main != null)
            {
                _headTransform = Camera.main.transform;
                _lastPosition = GetFlatPosition(_headTransform.position);
            }
        }

        private void Update()
        {
            if (_headTransform == null || _footstepClips == null || _footstepClips.Length == 0) return;

            Vector3 currentPos = GetFlatPosition(_headTransform.position);
            float delta = Vector3.Distance(currentPos, _lastPosition);

            // 0.005f threshold prevents floating point jitter from slowly accumulating a step
            if (delta > 0.005f)
            {
                _distanceMoved += delta;
                _lastPosition = currentPos;

                if (_distanceMoved >= _stepDistance)
                {
                    _distanceMoved = 0f;
                    PlayFootstep();
                }
            }
        }

        private Vector3 GetFlatPosition(Vector3 pos)
        {
            return new Vector3(pos.x, 0, pos.z);
        }

        private void PlayFootstep()
        {
            if (_soundService == null) return;

            AudioClip clip = _footstepClips[Random.Range(0, _footstepClips.Length)];
            
            // Randomize volume slightly for varied footstep sounds
            float randomizedVolume = _volume * Random.Range(0.8f, 1.2f);

            // Play at this transform's position, which should be the XR Origin's root (floor level).
            _soundService.PlaySoundFxAtPosition(clip, transform, randomizedVolume);
        }
    }
}
