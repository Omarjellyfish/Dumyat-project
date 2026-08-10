using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Bridges a completed purchase to the notification HUD. A thin presenter in the same shape as
    /// <see cref="SaleScoreNotifier"/>: it holds no shop state and decides nothing about the sale, it
    /// only reports what <see cref="Shop"/> already did.
    ///
    /// Only successes get a card. A refusal is already answered on the panel itself — the button reads
    /// "SHORT 75" before it is ever pressed — and a HUD card for something the player was told about in
    /// advance is noise.
    /// </summary>
    public class ShopPurchaseNotifier : MonoBehaviour
    {
        [Tooltip("Small uppercase label above the card.")]
        [SerializeField] private string _kicker = "PURCHASED";

        [Tooltip("Delta jade — the project's colour for money moving.")]
        [SerializeField] private Color _accent = new Color(0.553f, 0.922f, 0.686f, 1f);

        private INotificationService _notifications;
        private IEventService _events;

        [Inject]
        private void Inject(INotificationService notifications, IEventService events)
        {
            _notifications = notifications;
            _events = events;
        }

        private void OnEnable()
        {
            if (_events == null)
            {
                Debug.LogWarning($"{nameof(ShopPurchaseNotifier)} on '{name}' has no IEventService injected — " +
                                 "purchases will not be announced. Does this scene have a ContainerScope?", this);
                return;
            }
            _events.Subscribe<ItemPurchasedEvent>(HandlePurchased);
        }

        private void OnDisable() => _events?.Unsubscribe<ItemPurchasedEvent>(HandlePurchased);

        // Cached method group so Unsubscribe matches (see CLAUDE.md, Events).
        private void HandlePurchased(ItemPurchasedEvent evt)
        {
            if (_notifications == null || evt.Product == null) return;

            // Buying five nails in a row is one trip to the shop, not five results worth reading in
            // turn, so the cards share a key and the latest supersedes the queued one. Scoped to the
            // product, so buying nails then glue still gets a card each.
            string coalesceKey = $"shop-purchase:{evt.Product.ItemID}";

            _notifications.Enqueue(new NotificationData(
                evt.Product.DisplayName,
                $"−{evt.Price} EGP",
                evt.Product.Icon,
                _accent,
                -1,                          // no score chip
                NotificationStyle.Toast,     // frequent and low-ceremony
                _kicker,
                coalesceKey));
        }
    }
}
