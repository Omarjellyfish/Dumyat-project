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

        /// <summary>
        /// Retires every notification carrying <paramref name="coalesceKey"/> — queued ones are dropped and
        /// the one on screen fades out early — because what it was reporting has been overtaken.
        ///
        /// This is the counterpart to <see cref="NotificationData.CoalesceKey"/>. Coalescing keeps a burst
        /// of updates to ONE card; this retires that card when the last update in the burst is one the
        /// sender chose not to show. Without it the card is left frozen on the second-to-last count: a
        /// four-fish sale finishing a "sell 4" quest held "3 / 4" on screen while the quest was already
        /// complete, because the 4/4 tick was suppressed in favour of the completion medal.
        /// </summary>
        void Dismiss(string coalesceKey);
    }
}
