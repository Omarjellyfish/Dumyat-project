using UnityEngine;
using Reflex.Attributes;
namespace DumyatRuntime
{
    [RequireComponent(typeof(Collider))]
    public class AmbientZoneTrigger : MonoBehaviour
    {
        [Header("Zone Audio Settings")]
        [Tooltip("The looping ambient clip for this specific area.")]
        [SerializeField] private AudioClip zoneAmbience;

        [Tooltip("How long it takes to fade into this track upon entering.")]
        [SerializeField] private float fadeDuration = 3.0f;

        private IWorldAudioService _worldAudio;

        [Inject]
        private void Inject(IWorldAudioService worldAudio) => _worldAudio = worldAudio;

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
                _worldAudio?.PlayAmbient(zoneAmbience, fadeDuration);
        }
    }
}