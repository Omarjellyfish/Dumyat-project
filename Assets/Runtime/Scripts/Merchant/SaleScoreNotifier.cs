using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Bridges a completed sale to the generic notification HUD: on <see cref="SellScoreEvent"/> it maps the
    /// 0-100 selling score to a funny rank (<see cref="NotificationTierDatabaseSO"/>) and enqueues it via
    /// <see cref="INotificationService"/>. Keeps the notification system sale-agnostic — mirrors the thin-
    /// presenter pattern of <see cref="MerchantSubtitlePresenter"/>.
    ///
    /// Drop on any GameObject in a scene that has a <see cref="NotificationHudManager"/> registered and a
    /// ContainerScope. Assign the tier database asset.
    /// </summary>
    public class SaleScoreNotifier : MonoBehaviour
    {
        [Tooltip("Score-band → rank (title/icon/description/accent) mapping asset.")]
        [SerializeField] private NotificationTierDatabaseSO _tiers;

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
                Debug.LogWarning($"{nameof(SaleScoreNotifier)} on '{name}' has no IEventService injected — " +
                                 "sale notifications will not fire. Does this scene have a ContainerScope, and " +
                                 "is a NotificationHudManager registered?", this);
                return;
            }
            _events.Subscribe<SellScoreEvent>(HandleSellScore);
        }

        private void OnDisable() => _events?.Unsubscribe<SellScoreEvent>(HandleSellScore);

        private void HandleSellScore(SellScoreEvent evt)
        {
            if (_notifications == null)
            {
                Debug.LogWarning($"{nameof(SaleScoreNotifier)} on '{name}': no INotificationService — cannot " +
                                 "show the selling-score notification.", this);
                return;
            }
            if (_tiers == null)
            {
                Debug.LogWarning($"{nameof(SaleScoreNotifier)} on '{name}': no NotificationTierDatabase " +
                                 "assigned — cannot resolve a rank for the score.", this);
                return;
            }
            _notifications.Enqueue(_tiers.Resolve(evt.Score));
        }

        /// <summary>
        /// Debug: enqueues one notification per rank (through the real HUD) so every tier can be reviewed in
        /// order. They queue FIFO and each shows for the HUD's hold time. Right-click the component ▸ this,
        /// while in Play mode. Also callable at runtime (e.g. from a VR debug button).
        /// </summary>
        [ContextMenu("Debug ▸ Preview All Sale Tiers")]
        public void DebugPreviewAllTiers()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning($"{nameof(SaleScoreNotifier)}: enter Play mode to preview tiers — the HUD is " +
                                 "built at runtime.", this);
                return;
            }
            if (_notifications == null || _tiers == null)
            {
                Debug.LogWarning($"{nameof(SaleScoreNotifier)} on '{name}': cannot preview — missing " +
                                 "INotificationService or tier database.", this);
                return;
            }
            foreach (NotificationData n in _tiers.PreviewAll())
                _notifications.Enqueue(n);
        }
    }
}
