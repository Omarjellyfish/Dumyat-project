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

            if(other.CompareTag("Player"))
                WorldAudioManager.Singleton.PlayAmbient(zoneAmbience, fadeDuration);
             WorldAudioManager.Singleton.DoesExist();
        }
    }
}