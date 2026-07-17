using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Serialization;

namespace DumyatRuntime
{
    /// <summary>
    /// A single fish swimming in the water, ready to be caught and sold. Its species is authored on
    /// the instance (<see cref="Species"/>); its weight and its per-kg market rate are rolled ONCE at
    /// <c>Awake</c> from that species' bands, so the fish already has a definite identity before the
    /// player ever hooks it — catching only reveals it, it never re-rolls it.
    ///
    /// <see cref="ToFishData"/> converts it to the merchant's <see cref="FishData"/>; the sale itself
    /// is negotiated by the merchant system, which owns all pricing.
    /// </summary>
    public class Fish : MonoBehaviour
    {
        [Tooltip("Which species this fish is. Authored per instance — every fish in the water has a " +
                 "species before it is caught. Assign a FishSpeciesSO from Assets/Runtime/Content/Fish.")]
        [SerializeField] private FishSpeciesSO _species;

        [Tooltip("The fish's mouth (empty/bone) — where the hook attaches.")]
        [SerializeField, FormerlySerializedAs("mouthPoint")] private Transform _mouthPoint;

        // Rolled once at Awake and never again: this fish's honest caught weight (kg) and the market
        // rate its species fetches today. Together they give the fair market value the merchant
        // negotiates against.
        private float _weight;
        private float _pricePerKg;
        private bool _isCaught;

        private static readonly int HookedHash = Animator.StringToHash("isHooked");

        private Animator _animator;
        private FishData _fishData;
        private IEventService _eventService;

        [Inject]
        private void Inject(IEventService eventService) => _eventService = eventService;

        public FishSpeciesSO Species => _species;

        /// <summary>This fish's weight (kg) — decided at spawn, not at catch.</summary>
        public float Weight => _weight;

        public bool IsCaught => _isCaught;

        public Transform MouthPoint => _mouthPoint;

        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>();

            if (_species == null)
            {
                Debug.LogWarning($"Fish '{name}' has no FishSpeciesSO assigned — it cannot be sold. " +
                                 "Assign one in the Inspector.", this);
                return;
            }

            _weight = Mathf.Round(_species.RollWeight() * 100f) / 100f;   // 2dp — reads cleanly in prompts
            _pricePerKg = _species.RollPricePerKg();
        }

        private void Start()
        {
            SetHooked(false);
        }

        public void StopAnimation()
        {
            SetHooked(true);
        }

        /// <summary>
        /// This fish as the merchant's <see cref="FishData"/>. Built once and reused so the object
        /// handed to the negotiation stays reference-identical all the way to the sale.
        /// Returns null when no species is assigned.
        /// </summary>
        public FishData ToFishData()
        {
            if (_species == null) return null;

            return _fishData ??= new FishData
            {
                fishType = _species.FishType,
                fishTypeArabic = _species.FishTypeArabic,
                weight = _weight,
                fairMarketValue = Mathf.Round(_weight * _pricePerKg)
            };
        }

        /// <summary>
        /// Called by <see cref="HookingFish"/> when the hook takes this fish. Announces the catch so
        /// the player's fish inventory can pick it up; the weight/value were already decided at Awake.
        /// </summary>
        public void OnCaught()
        {
            if (_isCaught) return;
            _isCaught = true;

            if (_species == null)
            {
                Debug.LogWarning($"Fish '{name}' was caught with no species assigned — " +
                                 "not adding it to the fish inventory.", this);
                return;
            }

            Debug.Log($"Caught {_species.FishType} ({_species.FishTypeArabic}) — " +
                      $"{_weight:0.##}kg, fair value {ToFishData().fairMarketValue:0.#}.");

            if (_eventService == null)
            {
                Debug.LogWarning($"Fish '{name}' caught but no IEventService was injected — the catch " +
                                 "will not reach the fish inventory. Does this scene have a ContainerScope?", this);
                return;
            }

            _eventService.Publish(new FishCaughtEvent(this));
        }

        private void SetHooked(bool hooked)
        {
            if (_animator == null) return;
            _animator.SetBool(HookedHash, hooked);
        }
    }
}
