using System.Collections;
using System.Collections.Generic;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Order in which auto-approaching merchants are selected from the roster.
    /// </summary>
    public enum ApproachOrder
    {
        /// <summary>Pick a random available merchant each time (avoiding an immediate repeat).</summary>
        Random,

        /// <summary>Cycle through available merchants in roster order (round-robin).</summary>
        Sequential
    }

    /// <summary>
    /// Orchestrates WHICH merchant approaches the player and WHEN, at the El Borg fish
    /// market. It does not run the negotiation itself — it only enforces "one active
    /// merchant at a time", picks the next merchant, and manages the cooldown between
    /// approaches. Merchant GameObjects are assumed to already exist in the scene (each
    /// with a <see cref="MerchantNPC"/>); this never instantiates new ones.
    /// </summary>
    public class MerchantSpawner : MonoBehaviour
    {
        // Injected negotiation orchestrator (replaces the former NegotiationFlowController.Singleton).
        private INegotiationService _negotiation;

        [Inject]
        private void Inject(INegotiationService negotiation) => _negotiation = negotiation;

        [Header("Config")]
        [Tooltip("Global merchant system config (roster size, cooldown, price ranges).")]
        [SerializeField] private MerchantSystemConfig _systemConfig;

        [Tooltip("How auto-approaching merchants are chosen from the roster.")]
        [SerializeField] private ApproachOrder _approachOrder = ApproachOrder.Random;

        [Header("Scene References")]
        [Tooltip("Merchant NPCs already placed in the market scene. The spawner picks from these.")]
        [SerializeField] private List<MerchantNPC> _merchants = new List<MerchantNPC>();

        [Tooltip("Tag used to identify the player when they enter the market trigger volume.")]
        [SerializeField] private string _playerTag = "Player";

        // STUB SEAM: real integration should query the inventory system, e.g.
        // InventorySystem.GetFishItems().Count > 0. Until that's wired, this serialized
        // toggle drives fish detection so the approach orchestration can be exercised
        // in-editor. Replace PlayerHasFish() body when connecting the inventory.
        [Header("Stubs (temporary)")]
        [Tooltip("STUB: stands in for InventorySystem.GetFishItems(). Remove once inventory is wired.")]
        [SerializeField] private bool _debugPlayerHasFish = true;

        // --- State lock: only ONE merchant may approach/negotiate at any time. ---
        private MerchantNPC _activeMerchant;
        private bool _playerInMarket;
        // True once a merchant has engaged the player during the CURRENT market visit. Once set, no new
        // AUTO-approach starts until the player leaves and re-enters — a conversation never restarts on
        // its own while the player lingers. Reset on a fresh market entry (OnTriggerEnter).
        private bool _approachedThisVisit;
        private int _sequentialIndex;
        private MerchantNPC _lastApproached;
        private Coroutine _cooldownRoutine;

        // Cached approach-movers for the roster (null entries allowed for merchants without one), so
        // IsAnyMerchantEngaging can cheaply check whether ANY merchant is currently in motion.
        private readonly Dictionary<MerchantNPC, MerchantApproachMover> _movers =
            new Dictionary<MerchantNPC, MerchantApproachMover>();

        /// <summary>True while a merchant is actively negotiating with the player.</summary>
        public bool IsNegotiationActive => _activeMerchant != null;

        /// <summary>The merchant currently engaged with the player, or null if none.</summary>
        public MerchantNPC ActiveMerchant => _activeMerchant;

        /// <summary>
        /// True when the player is already being engaged by SOME merchant — either a negotiation is
        /// active, or a merchant is currently walking to/from the player. This is the real "one NPC at
        /// a time" gate: a second merchant must not even START approaching while this is true (the
        /// negotiation lock alone is not enough, because it is only taken once a merchant ARRIVES —
        /// without this check two merchants would walk over together and the loser would only be turned
        /// back on arrival).
        /// </summary>
        public bool IsAnyMerchantEngaging()
        {
            if (IsNegotiationActive) return true;
            foreach (MerchantNPC merchant in _merchants)
            {
                MerchantApproachMover mover = ResolveMover(merchant);
                if (mover != null && mover.IsBusy) return true;
            }
            return false;
        }

        private MerchantApproachMover ResolveMover(MerchantNPC merchant)
        {
            if (merchant == null) return null;
            if (_movers.TryGetValue(merchant, out MerchantApproachMover cached)) return cached;
            MerchantApproachMover mover = merchant.GetComponent<MerchantApproachMover>();
            _movers[merchant] = mover;   // cache even null so we don't GetComponent every frame
            return mover;
        }

        private void Start()
        {
            ValidateRosterAgainstConfig();
        }

        // --- Roster validation (point 1) -------------------------------------------

        // The scene NPC list is the live roster (point 2 requires using pre-placed
        // NPCs). MerchantSystemConfig is read here purely as a design-time guard: warn
        // when the scene doesn't match the authored config so misconfiguration surfaces
        // in the Console instead of silently changing who can approach.
        private void ValidateRosterAgainstConfig()
        {
            if (_systemConfig == null)
            {
                Debug.LogWarning("MerchantSpawner: no MerchantSystemConfig assigned — " +
                                 "using scene NPC list unchecked, and cooldown defaults to 0.");
                return;
            }

            int sceneCount = 0;
            foreach (MerchantNPC merchant in _merchants)
            {
                if (merchant == null) continue;
                sceneCount++;

                if (merchant.Config == null)
                {
                    Debug.LogWarning($"MerchantSpawner: scene merchant '{merchant.name}' has no " +
                                     "MerchantConfig assigned.");
                }
                else if (!ConfigIsRegistered(merchant.Config))
                {
                    Debug.LogWarning($"MerchantSpawner: scene merchant '{NameOf(merchant)}' uses a " +
                                     "MerchantConfig not listed in MerchantSystemConfig.MerchantConfigs.");
                }
            }

            if (sceneCount != _systemConfig.MerchantCount)
            {
                Debug.LogWarning($"MerchantSpawner: scene has {sceneCount} merchant(s) but " +
                                 $"MerchantSystemConfig.MerchantCount is {_systemConfig.MerchantCount}.");
            }
        }

        private bool ConfigIsRegistered(MerchantConfig config)
        {
            IReadOnlyList<MerchantConfig> configs = _systemConfig.MerchantConfigs;
            for (int i = 0; i < configs.Count; i++)
            {
                if (configs[i] == config) return true;
            }
            return false;
        }

        // --- Market entry/exit detection (point 3) ---------------------------------

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(_playerTag)) return;

            _playerInMarket = true;
            _approachedThisVisit = false;   // fresh visit: allow one approach again
            TryStartAutoApproach();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(_playerTag)) return;

            // Player left the market: stop auto-cycling. An in-progress negotiation is
            // left untouched (the negotiation system decides how to handle the player
            // physically walking away); we just won't queue the next auto-approach.
            _playerInMarket = false;
        }

        // --- Auto-approach flow ----------------------------------------------------

        private void TryStartAutoApproach()
        {
            if (IsAnyMerchantEngaging()) return;   // one at a time: nobody negotiating OR approaching
            if (!_playerInMarket) return;
            if (_approachedThisVisit) return;      // one conversation per visit — wait for a re-entry
            if (!PlayerHasFish()) return;

            MerchantNPC next = PickNextMerchant();
            if (next == null)
            {
                Debug.LogWarning("MerchantSpawner: no available merchant to approach.");
                return;
            }

            BeginNegotiationWith(next);
        }

        /// <summary>
        /// Lets an external interaction/proximity system route a player-initiated approach
        /// to a specific merchant (point 7). If a negotiation is already active, the request
        /// is REJECTED (not queued) — see comment below for the rationale.
        /// </summary>
        public void PlayerInitiatesWithMerchant(MerchantNPC merchant)
        {
            if (merchant == null)
            {
                Debug.LogWarning("MerchantSpawner: PlayerInitiatesWithMerchant called with null merchant.");
                return;
            }

            // POINT 8 — direct approach while another merchant is mid-negotiation:
            // We REJECT rather than queue. Rationale: queuing a direct approach captures a
            // moment of player intent that often goes stale by the time the current
            // negotiation ends (in VR the player may have physically walked elsewhere), and
            // a delayed merchant suddenly approaching a now-unrelated spot breaks immersion.
            // Rejecting keeps the "one at a time" lock honest and side-effect free; the
            // interaction system can simply let the player try again once the merchant frees up.
            if (IsNegotiationActive)
            {
                Debug.Log($"MerchantSpawner: rejecting direct approach to " +
                          $"'{NameOf(merchant)}' — '{NameOf(_activeMerchant)}' is mid-negotiation.");
                return;
            }

            if (!PlayerHasFish())
            {
                Debug.Log("MerchantSpawner: player initiated contact but is carrying no fish to sell.");
                return;
            }

            BeginNegotiationWith(merchant);
        }

        /// <summary>
        /// Called by <see cref="MerchantProximityTrigger"/> when the player physically walks out of a
        /// merchant's conversation range. If that merchant is the one currently negotiating, the
        /// negotiation is cancelled (the merchant walks away); otherwise it is ignored.
        /// </summary>
        public void PlayerLeftConversationArea(MerchantNPC merchant)
        {
            if (merchant == null || merchant != _activeMerchant) return;

            Debug.Log($"MerchantSpawner: player left '{NameOf(merchant)}' conversation area — cancelling negotiation.");
            if (_negotiation != null)
                _negotiation.CancelNegotiation(merchant);
            else
                merchant.EndNegotiation(NegotiationPhase.WalkedAway); // no controller: release the lock directly
        }

        private void BeginNegotiationWith(MerchantNPC merchant)
        {
            _activeMerchant = merchant;          // engages the state lock
            _approachedThisVisit = true;         // this visit's conversation is used up until re-entry
            _lastApproached = merchant;
            merchant.OnNegotiationEnded += HandleNegotiationEnded;
            merchant.BeginApproach();

            // Hand off to the flow controller, which runs the actual negotiation (voice/AI or a
            // scripted fallback) and signals completion back through merchant.EndNegotiation(...),
            // which fires OnNegotiationEnded -> HandleNegotiationEnded (releases the lock + cooldown).
            if (_negotiation != null)
            {
                _negotiation.BeginNegotiation(merchant);
            }
            else
            {
                // Without a controller the negotiation would never end and the lock would stick,
                // wedging the spawner. Fail loud and release immediately so the player isn't stuck.
                Debug.LogWarning("MerchantSpawner: no INegotiationService injected; " +
                                 "ending negotiation immediately to avoid a stuck state lock.");
                merchant.EndNegotiation(NegotiationPhase.Declined);
            }
        }

        private void HandleNegotiationEnded(MerchantNPC merchant, NegotiationPhase outcome)
        {
            merchant.OnNegotiationEnded -= HandleNegotiationEnded;

            if (merchant == _activeMerchant)
            {
                _activeMerchant = null;          // releases the state lock
            }

            Debug.Log($"MerchantSpawner: negotiation with '{NameOf(merchant)}' ended ({outcome}). " +
                      $"No new approach until the player leaves the market and returns.");

            // A conversation is one-per-visit: we do NOT auto-start another while the player lingers.
            // The cooldown pass still runs, but TryStartAutoApproach is gated by _approachedThisVisit,
            // so the next approach only happens after the player leaves and re-enters (OnTriggerEnter).
            if (_cooldownRoutine != null) StopCoroutine(_cooldownRoutine);
            _cooldownRoutine = StartCoroutine(CooldownThenAutoApproach());
        }

        private IEnumerator CooldownThenAutoApproach()
        {
            float cooldown = _systemConfig != null ? _systemConfig.CooldownBetweenApproaches : 0f;
            if (cooldown > 0f) yield return new WaitForSeconds(cooldown);

            _cooldownRoutine = null;
            TryStartAutoApproach();
        }

        // --- Merchant selection ----------------------------------------------------

        private MerchantNPC PickNextMerchant()
        {
            List<MerchantNPC> available = GetAvailableMerchants();
            if (available.Count == 0) return null;

            switch (_approachOrder)
            {
                case ApproachOrder.Sequential:
                    // Round-robin across the available subset.
                    MerchantNPC chosen = available[_sequentialIndex % available.Count];
                    _sequentialIndex++;
                    return chosen;

                case ApproachOrder.Random:
                default:
                    // Avoid an immediate repeat when more than one merchant is available.
                    if (available.Count > 1 && _lastApproached != null)
                    {
                        available.Remove(_lastApproached);
                    }
                    return available[Random.Range(0, available.Count)];
            }
        }

        private List<MerchantNPC> GetAvailableMerchants()
        {
            var available = new List<MerchantNPC>(_merchants.Count);
            foreach (MerchantNPC merchant in _merchants)
            {
                if (merchant != null && merchant.IsAvailable)
                {
                    available.Add(merchant);
                }
            }
            return available;
        }

        // --- Fish detection (stub seam) -------------------------------------------

        private bool PlayerHasFish()
        {
            // STUB: replace with InventorySystem.GetFishItems().Count > 0 once available.
            return _debugPlayerHasFish;
        }

        private static string NameOf(MerchantNPC merchant)
        {
            if (merchant == null) return "<none>";
            return merchant.Config != null ? merchant.Config.MerchantName : merchant.name;
        }
    }
}
