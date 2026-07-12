using UnityEngine;

namespace DumyatRuntime
{
    public class HookFishDetector : MonoBehaviour
    {
        [Header("References")]
        public FishArea fishArea;

        [Header("Detection")]
        public float fishDetectionRadius = 8f;

        private bool fishAlreadyCalled;

        private void OnTriggerEnter(Collider other)
        {
            if (fishAlreadyCalled)
                return;

            if (!other.CompareTag("Water"))
                return;

            Debug.Log("Hook entered: " + other.name);
            FishBehaviour nearestFish =
                fishArea.GetNearestFish(
                    transform.position,
                    fishDetectionRadius);

            if (nearestFish != null)
            {
                nearestFish.InvestigateHook(transform);

                Debug.Log(
                    "Nearest fish found: " +
                    nearestFish.name);
            }

            fishAlreadyCalled = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Water"))
            {
                fishAlreadyCalled = false;
            }
        }
    }
}
