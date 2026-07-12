using System.Collections.Generic;

namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for recording completed fish sales for progression, UI and the fish-sell
    /// process. Injected in place of the former <c>PlayerStats.Singleton</c>. Backed by
    /// <see cref="PlayerStats"/>. Each recorded sale is broadcast via <see cref="FishSaleRecordedEvent"/>
    /// on the event bus (subscribe to that rather than polling).
    /// </summary>
    public interface IPlayerStatsService
    {
        /// <summary>Every completed sale this save, oldest first.</summary>
        IReadOnlyList<FishSaleRecord> Sales { get; }

        /// <summary>Total money earned across all recorded sales.</summary>
        float TotalEarnings { get; }

        /// <summary>Average success rate (0–100) across all recorded sales, or 0 if none yet.</summary>
        float AverageSuccessRate { get; }

        /// <summary>
        /// Records one completed fish sale (call only on an ACCEPTED deal), persists it, and publishes
        /// <see cref="FishSaleRecordedEvent"/>. Ignores a null record.
        /// </summary>
        void RecordSale(FishSaleRecord record);
    }
}
