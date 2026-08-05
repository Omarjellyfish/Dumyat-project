using System;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Thin component placed on each pre-existing merchant GameObject in the market
    /// scene. For now it only carries the merchant's <see cref="MerchantConfig"/> and
    /// exposes the seams the <see cref="MerchantApproachCoordinator"/> needs to orchestrate
    /// approaches. The actual negotiation logic is intentionally NOT here yet — it
    /// will be added in the next step and will drive <see cref="EndNegotiation"/>.
    /// </summary>
    public class MerchantNPC : MonoBehaviour
    {
        [Tooltip("Archetype/config that defines this merchant's personality and economics.")]
        [SerializeField] private MerchantConfig _config;

        [Tooltip("If false, the coordinator will skip this merchant when picking who approaches next.")]
        [SerializeField] private bool _isAvailable = true;

        [Tooltip("This merchant's physical fish scale — the source of truth for which fish the player " +
                 "is selling. The merchant can only recognize and buy fish resting on it. Auto-resolved " +
                 "from children if left empty.")]
        [SerializeField] private FishSellScale _sellScale;

        [Tooltip("The visual body that should carry the merchant's facing rotation. Approach/turn logic " +
                 "rotates THIS transform instead of the merchant root, so siblings like the sell scale " +
                 "don't rotate with him. Auto-resolved to a child named 'Character', else the root.")]
        [SerializeField] private Transform _characterRoot;

        /// <summary>The archetype configuration for this merchant.</summary>
        public MerchantConfig Config => _config;

        /// <summary>Whether this merchant can currently be picked to approach the player.</summary>
        public bool IsAvailable => _isAvailable;

        /// <summary>
        /// This merchant's sell scale (source of truth for the fish being sold), or null if none is
        /// wired — in which case the negotiation falls back to the legacy single-fish path.
        /// </summary>
        public ISellScale SellScale => _sellScale;

        /// <summary>
        /// The transform that carries the merchant's facing rotation and his Animator — his visual body —
        /// so siblings like the sell scale stay put when he turns to face the player. Never null; falls
        /// back to the root if the rig has no 'Character' child.
        ///
        /// Resolved ON FIRST ACCESS rather than in Awake. Awake would be a race: MerchantApproachMover
        /// and MerchantAnimationDriver both read this from their OWN Awake, and Unity does not order
        /// Awake between components of one GameObject — so whoever ran first could silently get the root
        /// instead of the character, rotating the sell scale with the merchant and hunting for an
        /// Animator in the wrong place.
        /// </summary>
        public Transform CharacterRoot
        {
            get
            {
                if (_characterRoot == null) ResolveCharacterRoot();
                return _characterRoot;
            }
        }

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
        /// The coordinator subscribes to this to release its state lock and start the cooldown.
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
            // Fall back to a scale nested under this merchant if the field was left unassigned.
            if (_sellScale == null) _sellScale = GetComponentInChildren<FishSellScale>(true);

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

        // Finds the visual body: a descendant named 'Character' (it is nested under the rig, not always a
        // direct child), else the root. Cached into _characterRoot, so this runs once.
        private void ResolveCharacterRoot()
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Character") continue;
                _characterRoot = t;
                return;
            }

            _characterRoot = transform;
            Debug.LogWarning($"MerchantNPC '{name}': no 'Character' child found — facing rotation will " +
                             "fall back to the root (the sell scale will rotate with him) and the Animator " +
                             "will be searched for across the whole rig. Assign Character Root explicitly.",
                             this);
        }

        /// <summary>
        /// Called by the coordinator to tell this merchant to begin approaching/greeting the
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
        /// so the coordinator's cooldown/lock flow can be wired and tested.
        /// </summary>
        public void EndNegotiation(NegotiationPhase outcome)
        {
            OnNegotiationEnded?.Invoke(this, outcome);
        }
    }
}
