using UnityEngine;
namespace DumyatRuntime
{
    public class HookFishDetector : MonoBehaviour
    {
        [Header("References")]
        public FishArea fishArea;
        [SerializeField] private HookingFish hookingFish;
        [Header("Detection")]
        public float fishDetectionRadius = 8f;
        private bool fishAlreadyCalled;

        private FishBehaviour investigatingFish;

        private void Awake()
        {
            if (hookingFish == null)
            {
                hookingFish = GetComponent<HookingFish>();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (fishAlreadyCalled)
                return;
            if (!other.CompareTag("Water"))
                return;
            Debug.Log("Hook entered: " + other.name);

            // A new fishing attempt is starting, so let HookingFish
            // accept a fresh catch (otherwise isFishHooked stays true
            // from the previous catch and the next fish never gets a
            // FixedJoint, causing it to fall).
            hookingFish?.ResetHook();

            FishBehaviour nearestFish =
                fishArea.GetNearestFish(
                    transform.position,
                    fishDetectionRadius);
            if (nearestFish != null)
            {
                nearestFish.InvestigateHook(transform);
                investigatingFish = nearestFish;

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

                if (investigatingFish != null)
                {
                    investigatingFish.CancelHookInvestigation();
                    investigatingFish = null;
                }
            }
        }
    }
}