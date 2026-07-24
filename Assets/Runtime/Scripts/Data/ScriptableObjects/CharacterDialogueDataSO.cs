using System;
using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Per-character voice line bank. One asset per speaking character.
    /// Create via: Right-click → Create → Dumyat/Narrative/Character Dialogue Data.
    /// </summary>
    [CreateAssetMenu(fileName = "New Character", menuName = "Dumyat/Narrative/Character Dialogue Data")]
    public class CharacterDialogueDataSO : ScriptableObject
    {
        [Tooltip("Unique character identifier (e.g. 'Khalid', 'Narrator')")]
        public string CharacterId;

        [Tooltip("Display name shown in subtitles")]
        public string DisplayName;

        [Tooltip("All voice lines for this character, keyed by line ID")]
        public List<VoiceLineEntry> VoiceLines = new();

        /// <summary>
        /// Looks up an AudioClip by its line key. Returns null if not found.
        /// </summary>
        public AudioClip GetClip(string lineKey)
        {
            if (string.IsNullOrEmpty(lineKey)) return null;

            for (int i = 0; i < VoiceLines.Count; i++)
            {
                if (VoiceLines[i].LineKey == lineKey)
                    return VoiceLines[i].Clip;
            }
            return null;
        }
    }

    [Serializable]
    public class VoiceLineEntry
    {
        [Tooltip("Key referenced by DialogueLine.VoiceLineKey")]
        public string LineKey;
        public AudioClip Clip;
    }
}
