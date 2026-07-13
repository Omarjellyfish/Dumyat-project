using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Data asset representing a travel destination.
    /// Create one per destination: Right-click → Create → Dumyat → Travel Destination.
    /// </summary>
    [CreateAssetMenu(fileName = "New Destination", menuName = "Dumyat/Travel Destination")]
    public class TravelDestination : ScriptableObject
    {
        [Tooltip("Display name shown to the player in the destination list")]
        public string DisplayName;

        [Tooltip("The exact scene name as it appears in Build Settings")]
        public string SceneName;

        [TextArea(2, 4)]
        [Tooltip("Optional description shown in the selection UI")]
        public string Description;
    }
}
