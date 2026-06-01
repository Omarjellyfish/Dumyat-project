using UnityEngine;
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

        private void OnTriggerEnter(Collider other)
        {
            // In a networked game, you must verify this is the LOCAL player.
            // If another client's avatar walks into the zone, you don't want YOUR audio to change.
            
                WorldAudioManager.Singleton.PlayAmbient(zoneAmbience, fadeDuration);
        }
    }
}