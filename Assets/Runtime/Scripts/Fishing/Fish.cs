using UnityEngine;

namespace DumyatRuntime
{
    public class Fish : MonoBehaviour
    {
        public FishData data;

        public float currentWeight;
        public float currentValue;
        private bool isCaught;
        [SerializeField] private Transform mouthPoint; // drag mouth empty/bone here in Inspector

        public Transform MouthPoint => mouthPoint;

        private void Start()
        {
            Animator fishAnimator = GetComponentInChildren<Animator>();
            fishAnimator.SetBool("isHooked", false);
        }
        public void StopAnimation()
        {
            Animator fishAnimator = GetComponentInChildren<Animator>();
            fishAnimator.SetBool("isHooked", true);
        }
        public void Initialize(FishData fishData)
        {
            data = fishData;

            currentWeight = Random.Range(
                data.minWeight,
                data.maxWeight);

            currentValue = currentWeight * data.pricePerKg;
        }
        public void OnCaught()
        {
            if (isCaught)
                return;

            isCaught = true;

            if (data == null)
            {
                Debug.LogError("FishData is NULL!");
                return;
            }

            currentWeight = Random.Range(data.minWeight, data.maxWeight);
            currentValue = currentWeight * data.pricePerKg;

            Debug.Log($"Caught {data.fishName}");
            Debug.Log($"Weight: {currentWeight}");
            Debug.Log($"Value: {currentValue}");
        }
    }
}
