using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Trigger zone that starts a character walking through waypoints (via CharacterMoveOnly) the
    /// moment the Player enters it. Each instance drives exactly ONE character, so for a scene
    /// with multiple characters that should react independently, place one of these per character
    /// (each on its own zone collider, or all on the same zone with different _characterToMove
    /// values).
    /// </summary>
    public class CharacterMoveTriggerZone : MonoBehaviour
    {
        [Tooltip("The character to move once the player enters this zone.")]
        [SerializeField] private CharacterMoveOnly _characterToMove;

        [Tooltip("If true, this zone only fires once. If false, it fires every time the player enters.")]
        [SerializeField] private bool _triggerOnce = true;

        private bool _hasFired;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            if (_triggerOnce && _hasFired) return;

            if (_characterToMove == null)
            {
                Debug.LogWarning($"[{name}] No CharacterToMove assigned.", this);
                return;
            }

            _hasFired = true;
            Debug.Log($"[CharacterMoveTriggerZone] Player entered '{name}', moving {_characterToMove.name}.");
            _characterToMove.Move();
        }
    }
}