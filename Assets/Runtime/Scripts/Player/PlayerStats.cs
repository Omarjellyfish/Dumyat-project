using System;
using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Records completed fish sales for progression, UI and the fish-sell process. Each accepted deal
    /// is stored as a <see cref="FishSaleRecord"/> (which fish, its weight, the agreed price and the
    /// success score) and PERSISTED through the centralized save system: in <see cref="Awake"/> this
    /// points its list at <c>SaveManager.CurrentSaveData.salesData.sales</c> (per the project's save
    /// pattern), and every recorded sale triggers an async master-save.
    ///
    /// Downstream systems (inventory deduction, wallet/credit, UI) should subscribe to
    /// <see cref="OnSaleRecorded"/> rather than poll — keeping the sell process decoupled from the
    /// negotiation flow.
    /// </summary>
    public class PlayerStats : MonoBehaviour, IManager
    {
        public static PlayerStats Singleton { get; private set; }

        /// <summary>
        /// Fired when a fish sale is recorded (an accepted deal). Carries the authoritative sale record
        /// so the sell process / inventory / UI can react (credit money, remove the fish, refresh HUD).
        /// </summary>
        public static event Action<FishSaleRecord> OnSaleRecorded;

        // Points at the persisted list inside the master save (set in Awake); never held as a separate
        // copy, so mutating it mutates the save (project save convention).
        private List<FishSaleRecord> _sales;

        /// <summary>Every completed sale this save, oldest first.</summary>
        public IReadOnlyList<FishSaleRecord> Sales => _sales;

        /// <summary>Total money earned across all recorded sales.</summary>
        public float TotalEarnings
        {
            get
            {
                float sum = 0f;
                if (_sales != null)
                    foreach (FishSaleRecord s in _sales) sum += s.agreedPrice;
                return sum;
            }
        }

        /// <summary>Average success rate (0–100) across all recorded sales, or 0 if none yet.</summary>
        public float AverageSuccessRate
        {
            get
            {
                if (_sales == null || _sales.Count == 0) return 0f;
                float sum = 0f;
                foreach (FishSaleRecord s in _sales) sum += s.successRate;
                return sum / _sales.Count;
            }
        }

        private void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }
            Singleton = this;
            DontDestroyOnLoad(gameObject);

            // Ensure the master save is loaded, then point our list at the persisted sales sub-object
            // (mutating it mutates the save; SaveGameAsync persists it).
            if (SaveManager.CurrentSaveData == null) SaveManager.LoadGame();
            _sales = SaveManager.CurrentSaveData.salesData.sales;
        }

        /// <summary>
        /// Records one completed fish sale (call only on an ACCEPTED deal), persists it, and notifies
        /// listeners via <see cref="OnSaleRecorded"/>. Ignores a null record.
        /// </summary>
        public void RecordSale(FishSaleRecord record)
        {
            if (record == null)
            {
                Debug.LogWarning("PlayerStats: RecordSale called with a null record; ignored.");
                return;
            }

            if (_sales == null) _sales = SaveManager.CurrentSaveData.salesData.sales;
            _sales.Add(record);
            SaveManager.SaveGameAsync();

            Debug.Log($"PlayerStats: recorded sale of {record.fishType} {record.weight:0.##}kg for " +
                      $"{record.agreedPrice:0} EGP (success {record.successRate:0.#}; " +
                      $"{_sales.Count} sale(s), {TotalEarnings:0} EGP total).");

            OnSaleRecorded?.Invoke(record);
        }
    }
}
