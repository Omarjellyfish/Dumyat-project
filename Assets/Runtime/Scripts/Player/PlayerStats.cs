using System.Collections.Generic;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Records completed fish sales for progression, UI and the fish-sell process. Each accepted deal
    /// is stored as a <see cref="FishSaleRecord"/> (which fish, its weight, the agreed price and the
    /// success score) and PERSISTED through the centralized save system: it points its list at
    /// <c>ISaveService.CurrentSaveData.salesData.sales</c> (per the project's save pattern), and every
    /// recorded sale triggers an async master-save.
    ///
    /// Downstream systems (inventory deduction, wallet/credit, UI) should subscribe to
    /// <see cref="FishSaleRecordedEvent"/> on the event bus rather than poll — keeping the sell
    /// process decoupled from the negotiation flow.
    /// </summary>
    public class PlayerStats : MonoBehaviour, IPlayerStatsService
    {
        // Injected save store + event bus. A recorded sale is broadcast as FishSaleRecordedEvent
        // (replaces the former static OnSaleRecorded C# event); persistence goes through ISaveService
        // (replaces the former static SaveManager).
        private ISaveService _saveService;
        private IEventService _events;

        [Inject]
        private void Inject(ISaveService saveService, IEventService events)
        {
            _saveService = saveService;
            _events = events;
        }

        // Points at the persisted list inside the master save; never held as a separate copy, so
        // mutating it mutates the save (project save convention). Bound lazily because method
        // injection can land after Awake.
        private List<FishSaleRecord> _sales;

        // The persisted sales list, bound on first access from the injected save store.
        private List<FishSaleRecord> SalesList
        {
            get
            {
                if (_sales == null && _saveService != null)
                    _sales = _saveService.CurrentSaveData.salesData.sales;
                return _sales;
            }
        }

        /// <summary>Every completed sale this save, oldest first.</summary>
        public IReadOnlyList<FishSaleRecord> Sales => SalesList;

        /// <summary>Total money earned across all recorded sales.</summary>
        public float TotalEarnings
        {
            get
            {
                float sum = 0f;
                List<FishSaleRecord> sales = SalesList;
                if (sales != null)
                    foreach (FishSaleRecord s in sales) sum += s.agreedPrice;
                return sum;
            }
        }

        /// <summary>Average success rate (0–100) across all recorded sales, or 0 if none yet.</summary>
        public float AverageSuccessRate
        {
            get
            {
                List<FishSaleRecord> sales = SalesList;
                if (sales == null || sales.Count == 0) return 0f;
                float sum = 0f;
                foreach (FishSaleRecord s in sales) sum += s.successRate;
                return sum / sales.Count;
            }
        }

        /// <summary>
        /// Records one completed fish sale (call only on an ACCEPTED deal), persists it, and notifies
        /// listeners via <see cref="FishSaleRecordedEvent"/>. Ignores a null record.
        /// </summary>
        public void RecordSale(FishSaleRecord record)
        {
            if (record == null)
            {
                Debug.LogWarning("PlayerStats: RecordSale called with a null record; ignored.");
                return;
            }

            List<FishSaleRecord> sales = SalesList;
            if (sales == null)
            {
                Debug.LogWarning("PlayerStats: no ISaveService injected; cannot record the sale.");
                return;
            }

            sales.Add(record);
            _saveService.SaveAsync();

            Debug.Log($"PlayerStats: recorded sale of {record.fishType} {record.weight:0.##}kg for " +
                      $"{record.agreedPrice:0} EGP (success {record.successRate:0.#}; " +
                      $"{sales.Count} sale(s), {TotalEarnings:0} EGP total).");

            _events?.Publish(new FishSaleRecordedEvent(record));
        }
    }
}
