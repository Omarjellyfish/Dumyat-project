using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Minimal running record of how well the player does in merchant negotiations.
    ///
    /// STUB: created so <see cref="NegotiationFlowController"/> has a real seam to call. It keeps an
    /// in-memory list of success rates and an average. TODO: persist sale history via SaveManager /
    /// GameSaveData and surface it to UI/progression once those requirements are defined.
    /// </summary>
    public class PlayerStats : MonoBehaviour, IManager
    {
        public static PlayerStats Singleton { get; private set; }

        private readonly List<float> _saleSuccessRates = new List<float>();

        /// <summary>All recorded sale success rates (0–100), oldest first.</summary>
        public IReadOnlyList<float> SaleSuccessRates => _saleSuccessRates;

        /// <summary>Average success rate across all recorded sales, or 0 if none yet.</summary>
        public float AverageSuccessRate
        {
            get
            {
                if (_saleSuccessRates.Count == 0) return 0f;
                float sum = 0f;
                foreach (float rate in _saleSuccessRates) sum += rate;
                return sum / _saleSuccessRates.Count;
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
        }

        /// <summary>Records the outcome of one completed sale (success rate 0–100).</summary>
        public void RecordSale(float successRate)
        {
            _saleSuccessRates.Add(successRate);
            Debug.Log($"PlayerStats: recorded sale success {successRate:0.#} " +
                      $"(avg {AverageSuccessRate:0.#} over {_saleSuccessRates.Count} sale(s)).");
        }
    }
}
