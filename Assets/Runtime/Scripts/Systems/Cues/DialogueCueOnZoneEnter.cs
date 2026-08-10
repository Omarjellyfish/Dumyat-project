using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Speaks a <see cref="OneShotDialogueCue"/> the first time the player walks into this trigger.
    ///
    /// The replacement for <c>NarrativeTriggerZone</c>, whose "trigger once" was an instance bool that
    /// forgot itself on every scene load. Splitting the trigger from the line means the zone can sit on
    /// the same object as a <c>TutorialAnchor</c> the tutorial is already using as a zone, rather than
    /// needing a second collider in the same place.
    /// </summary>
    [AddComponentMenu("Dumyat/Cues/Dialogue Cue On Zone Enter")]
    [RequireComponent(typeof(Collider))]
    public class DialogueCueOnZoneEnter : MonoBehaviour
    {
        [Tooltip("The line to speak. Auto-resolved from this GameObject if left empty.")]
        [SerializeField] private OneShotDialogueCue _cue;

        private void Awake()
        {
            if (_cue == null) _cue = GetComponent<OneShotDialogueCue>();

            if (_cue == null)
                Debug.LogWarning($"{nameof(DialogueCueOnZoneEnter)} on '{name}' has no " +
                                 $"{nameof(OneShotDialogueCue)} to speak.", this);

            Collider trigger = GetComponent<Collider>();
            if (trigger != null && !trigger.isTrigger)
                Debug.LogWarning($"{nameof(DialogueCueOnZoneEnter)} on '{name}' has a collider that is " +
                                 "not a trigger — the player will bump into it instead of entering it.", this);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _cue?.TryPlay();
        }
    }
}
