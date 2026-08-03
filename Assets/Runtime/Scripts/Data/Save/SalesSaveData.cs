using System;
using System.Collections.Generic;

namespace DumyatRuntime
{
    /// <summary>
    /// Persisted history of completed fish sales (one <see cref="FishSaleRecord"/> per accepted deal).
    /// Owned by <c>PlayerStats</c> via the centralized save pattern and aggregated into
    /// <see cref="GameSaveData"/>, so the fish-sell process and inventory validation have an
    /// authoritative, saved record of what was sold, how heavy it was, and for how much.
    /// </summary>
    [Serializable]
    public class SalesSaveData
    {
        public List<FishSaleRecord> sales = new List<FishSaleRecord>();
    }
}
