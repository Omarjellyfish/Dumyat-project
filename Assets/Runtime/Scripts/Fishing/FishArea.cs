using UnityEngine;
using System.Collections.Generic;

namespace DumyatRuntime
{
    public class FishArea : MonoBehaviour
    {
        private readonly List<FishBehaviour> fishes = new();

        private BoxCollider areaCollider;
        public Rigidbody hookedRb;

        [Header("Leave Detection")]
        [Tooltip("How often (in seconds) to check whether fish have left the area bounds.")]
        public float leaveCheckInterval = 0.5f;

        private float leaveCheckTimer;

        private void Awake()
        {
            areaCollider = GetComponent<BoxCollider>();
        }

        private void Update()
        {
            leaveCheckTimer += Time.deltaTime;

            if (leaveCheckTimer >= leaveCheckInterval)
            {
                leaveCheckTimer = 0f;
                RemoveFishOutsideBounds();
            }
        }

        private void RemoveFishOutsideBounds()
        {
            if (areaCollider == null)
                return;

            Bounds bounds = areaCollider.bounds;

            for (int i = fishes.Count - 1; i >= 0; i--)
            {
                FishBehaviour fish = fishes[i];

                if (fish == null)
                {
                    fishes.RemoveAt(i);
                    continue;
                }

                if (!bounds.Contains(fish.transform.position))
                {
                    fish.SetInsideFishArea(false);

                    Debug.Log(
                        $"{fish.name} left the fish area and was removed from tracking.",
                        fish);

                    fishes.RemoveAt(i);
                }
            }
        }

        public void RegisterFish(FishBehaviour fish)
        {
            if (!fishes.Contains(fish))
            {
                fishes.Add(fish);
                Debug.Log($"{fish.name} registered. Total fish: {fishes.Count}", this);
            }
        }

        public void UnregisterFish(FishBehaviour fish)
        {
            fishes.Remove(fish);
        }

        public Bounds GetBounds()
        {
            return areaCollider.bounds;
        }

        public FishBehaviour GetNearestFish(
            Vector3 hookPosition,
            float maxDistance)
        {
            FishBehaviour nearestFish = null;

            float nearestDistance = maxDistance;
            Debug.Log($"GetNearestFish called on {name}, fish count: {fishes.Count}", this);


            foreach (FishBehaviour fish in fishes)
            {
                if (fish == null)
                    continue;

                float distance =
                    Vector3.Distance(
                        fish.transform.position,
                        hookPosition);
                Debug.Log($"{fish.name} is {distance:F1}m from hook (limit {maxDistance})", fish);

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestFish = fish;
                }
            }
            if (nearestFish != null)
            {
                hookedRb = nearestFish.GetComponent<Rigidbody>();
            }

            return nearestFish;
        }
        private void OnTriggerEnter(Collider other)
        {
            FishBehaviour fish = other.GetComponent<FishBehaviour>();

            if (fish != null)
            {
                fish.SetInsideFishArea(true);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            FishBehaviour fish = other.GetComponent<FishBehaviour>();

            if (fish != null)
            {
                fish.SetInsideFishArea(false);
            }
        }
    }
}