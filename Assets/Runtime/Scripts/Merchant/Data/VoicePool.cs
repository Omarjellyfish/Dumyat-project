using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Designer-authored pool of voices that merchant NPCs draw from. Each NPC is assigned ONE voice
    /// at spawn (by <see cref="MerchantSpawner"/>) and keeps it for its lifetime, so a merchant's
    /// voice never changes between TTS lines.
    ///
    /// LOCALE — CRITICAL (docs/MERCHANT_CONTEXT.md): every entry MUST be a verified Egyptian Arabic
    /// ElevenLabs voice, not generic Arabic/Gulf/Levantine/MSA.
    /// </summary>
    [CreateAssetMenu(fileName = "VoicePool", menuName = "Dumyat/Merchant/Voice Pool")]
    public class VoicePool : ScriptableObject
    {
        [Tooltip("ElevenLabs voice IDs. MUST be verified Egyptian Arabic voices (see docs/MERCHANT_CONTEXT.md).")]
        [SerializeField] private List<string> _voiceIds = new List<string>();

        /// <summary>All voice IDs in the pool.</summary>
        public IReadOnlyList<string> VoiceIds => _voiceIds;

        /// <summary>Number of voices in the pool.</summary>
        public int Count => _voiceIds.Count;

        /// <summary>Returns a random voice ID, or null if the pool is empty.</summary>
        public string GetRandom()
        {
            if (_voiceIds.Count == 0) return null;
            return _voiceIds[Random.Range(0, _voiceIds.Count)];
        }
    }
}
