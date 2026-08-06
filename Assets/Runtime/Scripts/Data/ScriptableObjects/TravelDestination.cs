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

        [Tooltip("Arabic display name, shown when the player's language setting is Arabic. Leave " +
                 "empty and the board falls back to DisplayName, so an untranslated stop still reads.")]
        public string DisplayNameArabic;

        [Tooltip("Short route code shown on the departure board, e.g. 'C1'. Gives a route a handle " +
                 "the player can learn, so after a few trips they point at the code instead of " +
                 "reading every description. Display only — leave empty to hide the code cell.")]
        public string RouteCode;

        [Tooltip("The exact scene name as it appears in Build Settings")]
        public string SceneName;

        [TextArea(2, 4)]
        [Tooltip("Optional description shown in the selection UI")]
        public string Description;

        [TextArea(2, 4)]
        [Tooltip("Arabic description, shown when the player's language setting is Arabic. Leave " +
                 "empty and the board falls back to Description.")]
        public string DescriptionArabic;

        [Tooltip("Optional persistence key required to see this destination in the list")]
        public string RequiredPersistenceKey;
    }
}
