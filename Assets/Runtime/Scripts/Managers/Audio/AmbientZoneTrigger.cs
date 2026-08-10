using UnityEngine;
using Reflex.Attributes;
namespace DumyatRuntime
{
    [RequireComponent(typeof(Collider))]
    public class AmbientZoneTrigger : MonoBehaviour
    {
        /// <summary>
        /// Ambient volumes are big, invisible and sit exactly where the player walks — which makes them
        /// perfect ray-blockers. One of these in Ras El Bar was standing between the player and the
        /// lower third of the boat board, so the top of the panel was hoverable and the button under it
        /// was not: a UI that looked alive and did nothing.
        ///
        /// They only ever need OnTriggerEnter, and trigger overlap uses the layer collision matrix, not
        /// the raycast mask — so Ignore Raycast costs them nothing and keeps them out of the way of
        /// every interaction ray. This warns rather than reassigning the layer, because silently moving
        /// a designer's object between layers is worse than telling them.
        /// </summary>
        private void Awake()
        {
            const int ignoreRaycast = 2;
            if (gameObject.layer != ignoreRaycast)
                Debug.LogWarning($"{nameof(AmbientZoneTrigger)} on '{name}' is on layer " +
                                 $"'{LayerMask.LayerToName(gameObject.layer)}'. Put it on 'Ignore Raycast' — " +
                                 "an ambient volume on a ray-tested layer will block interaction with " +
                                 "anything behind it.", this);
        }

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
            {
                Debug.Log("Player entered ambient zone");
                _worldAudio?.PlayAmbient(zoneAmbience, fadeDuration);
            }
        }
    }
}