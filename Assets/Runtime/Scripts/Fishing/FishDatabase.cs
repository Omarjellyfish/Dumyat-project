using UnityEngine;
using System.Collections.Generic;

namespace DumyatRuntime
{
    public class FishDatabase : MonoBehaviour
    {
        public List<FishyData> fishTypes = new List<FishyData>();

        private void Awake()
        {
            fishTypes.Add(new FishyData
            {
                fishName = "Tilapia",
                minWeight = 0.5f,
                maxWeight = 3f,
                pricePerKg = 50f
            });

            fishTypes.Add(new FishyData
            {
                fishName = "Catfish",
                minWeight = 1f,
                maxWeight = 8f,
                pricePerKg = 80f
            });

            fishTypes.Add(new FishyData
            {
                fishName = "Tuna",
                minWeight = 5f,
                maxWeight = 50f,
                pricePerKg = 200f
            });
        }

        public FishyData GetRandomFishType()
        {
            return fishTypes[Random.Range(0, fishTypes.Count)];
        }
    }
}
