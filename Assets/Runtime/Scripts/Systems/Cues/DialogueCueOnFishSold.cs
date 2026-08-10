using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Speaks a <see cref="OneShotDialogueCue"/> when the player closes a sale with a merchant.
    ///
    /// Keyed on <see cref="SellScoreEvent"/>, which is published once per accepted deal by every sale
    /// path there is — the AI negotiation's single-fish and bundle settlements and the scripted offline
    /// flow all raise it through <c>BundleSaleSettler</c>. Listening to the outcome rather than to a
    /// particular merchant script is what makes one cue cover all three; hanging it off the negotiation
    /// controller would have missed the offline sale entirely.
    ///
    /// It carries no once-only logic of its own. How often the line may repeat is the gate's business on
    /// the <see cref="OneShotDialogueCue"/> beside it, and which scenes it belongs to is the gate's
    /// allowed-scenes list — a bundle sale settles per sale, not per fish, so the gate is what stops a
    /// father who is not even in the market congratulating the player in another town.
    /// </summary>
    [AddComponentMenu("Dumyat/Cues/Dialogue Cue On Fish Sold")]
    public class DialogueCueOnFishSold : MonoBehaviour
    {
        [Tooltip("The line to speak. Auto-resolved from this GameObject if left empty.")]
        [SerializeField] private OneShotDialogueCue _cue;

        private IEventService _eventService;
        private bool _subscribed;

        [Inject]
        private void Inject(IEventService eventService)
        {
            _eventService = eventService;
            if (isActiveAndEnabled) Subscribe();
        }

        private void Awake()
        {
            if (_cue == null) _cue = GetComponent<OneShotDialogueCue>();

            if (_cue == null)
                Debug.LogWarning($"{nameof(DialogueCueOnFishSold)} on '{name}' has no " +
                                 $"{nameof(OneShotDialogueCue)} to speak.", this);
        }

        // Reflex injects scene objects from ContainerScope.Awake, which can land either side of this
        // object's OnEnable — hence subscribing from both, guarded, rather than betting on the order.
        private void OnEnable() => Subscribe();

        private void OnDisable()
        {
            if (!_subscribed) return;

            _eventService.Unsubscribe<SellScoreEvent>(OnSaleClosed);
            _subscribed = false;
        }

        private void Subscribe()
        {
            if (_subscribed || _eventService == null) return;

            _eventService.Subscribe<SellScoreEvent>(OnSaleClosed);
            _subscribed = true;
        }

        private void OnSaleClosed(SellScoreEvent evt) => _cue?.TryPlay();
    }
}
