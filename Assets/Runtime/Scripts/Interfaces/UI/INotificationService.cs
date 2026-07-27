namespace DumyatRuntime
{
    /// <summary>
    /// Generic, queued notification surface. Enqueue a {title, icon, description} card and it is shown on the
    /// world-space HUD for a fixed time, one after another (FIFO). Sale-agnostic — the merchant sale feeds it
    /// through <see cref="SaleScoreNotifier"/>, but any system can raise a notification.
    /// Implemented by <see cref="NotificationHudManager"/>.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>Adds a notification to the back of the queue. Shown once earlier ones finish.</summary>
        void Enqueue(NotificationData data);

        /// <summary>Drops every queued (not-yet-shown) notification. The one on screen finishes normally.</summary>
        void Clear();
    }
}
