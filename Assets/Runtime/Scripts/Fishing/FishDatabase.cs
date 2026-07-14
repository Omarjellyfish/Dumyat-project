using UnityEngine;
using System.Collections.Generic;

namespace DumyatRuntime
{
    public class FishDatabase : MonoBehaviour
    {
        public List<FishData> fishTypes = new List<FishData>();

        private void Awake()
        {
            fishTypes.Add(new FishData
            {
                fishName = "Tilapia",
                minWeight = 0.5f,
                maxWeight = 3f,
                pricePerKg = 50f
            });

            fishTypes.Add(new FishData
            {
                fishName = "Catfish",
                minWeight = 1f,
                maxWeight = 8f,
                pricePerKg = 80f
            });

            fishTypes.Add(new FishData
            {
                fishName = "Tuna",
                minWeight = 5f,
                maxWeight = 50f,
                pricePerKg = 200f
            });
        }

        public FishData GetRandomFishType()
        {
            return fishTypes[Random.Range(0, fishTypes.Count)];
        }
    }
}
