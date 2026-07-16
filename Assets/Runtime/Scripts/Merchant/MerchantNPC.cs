using System;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Thin component placed on each pre-existing merchant GameObject in the market
    /// scene. For now it only carries the merchant's <see cref="MerchantConfig"/> and
    /// exposes the seams the <see cref="MerchantSpawner"/> needs to orchestrate
    /// approaches. The actual negotiation logic is intentionally NOT here yet — it
    /// will be added in the next step and will drive <see cref="EndNegotiation"/>.
    /// </summary>
    public class MerchantNPC : MonoBehaviour
    {
        [Tooltip("Archetype/config that defines this merchant's personality and economics.")]
        [SerializeField] private MerchantConfig _config;

        [Tooltip("If false, the spawner will skip this merchant when picking who approaches next.")]
        [SerializeField] private bool _isAvailable = true;

        /// <summary>The archetype configuration for this merchant.</summary>
        public MerchantConfig Config => _config;

        /// <summary>Whether this merchant can currently be picked to approach the player.</summary>
        public bool IsAvailable => _isAvailable;

        private int _conversationCount;

        /// <summary>
        /// True until the first negotiation with this merchant has been registered. The opening line
        /// ALWAYS greets on the first conversation; afterwards a greeting is only sometimes used.
        /// </summary>
        public bool IsFirstConversation => _conversationCount == 0;

        /// <summary>Records that a negotiation has started with this merchant (call once per opening).</summary>
        public void RegisterConversation() => _conversationCount++;

        /// <summary>
        /// Raised when a negotiation with this merchant ends, carrying the final outcome.
        /// The spawner subscribes to this to release its state lock and start the cooldown.
        /// </summary>
        public event Action<MerchantNPC, NegotiationPhase> OnNegotiationEnded;

        /// <summary>
        /// Raised when this merchant is told to begin an approach/negotiation (see
        /// <see cref="BeginApproach"/>). Cosmetic listeners (animation, VFX) subscribe to this —
        /// e.g. <see cref="MerchantAnimationDriver"/> plays the greeting wave.
        /// </summary>
        public event Action<MerchantNPC> OnApproachStarted;

        /// <summary>
        /// The ElevenLabs voice ID this merchant speaks with for its lifetime, so its voice never
        /// changes between TTS lines. Sourced from the merchant's <see cref="MerchantConfig.VoiceId"/>.
        /// </summary>
        public string AssignedVoiceId { get; private set; }

        private void Awake()
        {
            // The merchant's voice is defined by its config archetype, and stays fixed for this NPC's
            // lifetime so its voice never changes between TTS lines.
            AssignedVoiceId = _config != null ? _config.VoiceId : null;

            // Catch mis-authored configs early: a null config or empty VoiceId means this NPC has no
            // stable voice, which otherwise only shows up as silent (subtitle-only) TTS at run time.
            if (_config == null)
                Debug.LogWarning($"MerchantNPC '{name}': no MerchantConfig assigned — it has no voice.");
            else if (string.IsNullOrWhiteSpace(AssignedVoiceId))
                Debug.LogWarning($"MerchantNPC '{name}' (config '{_config.MerchantName}'): MerchantConfig " +
                                 "has no ElevenLabs VoiceId; this merchant will have no voice.");
        }

        /// <summary>
        /// Called by the spawner to tell this merchant to begin approaching/greeting the
        /// player. Notifies cosmetic listeners via <see cref="OnApproachStarted"/> (the
        /// negotiation itself is driven by <see cref="NegotiationFlowController"/>).
        /// </summary>
        public void BeginApproach()
        {
            string who = _config != null ? _config.MerchantName : name;
            Debug.Log($"MerchantNPC: '{who}' begins approaching the player.");
            OnApproachStarted?.Invoke(this);
        }

        /// <summary>
        /// Ends the current negotiation with the given outcome and notifies subscribers.
        /// Temporary seam: the negotiation system (next step) will call this. Exposed now
        /// so the spawner's cooldown/lock flow can be wired and tested.
        /// </summary>
        public void EndNegotiation(NegotiationPhase outcome)
        {
            OnNegotiationEnded?.Invoke(this, outcome);
        }
    }
}
