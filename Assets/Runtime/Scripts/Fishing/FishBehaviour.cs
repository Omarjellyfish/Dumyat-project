using UnityEngine;
using System.Collections;

namespace DumyatRuntime
{
    public class FishBehaviour : MonoBehaviour
    {
        [Header("References")]
        public FishArea fishArea;
        // public Rigidbody rb;
        private Rigidbody fishRb;

        [Header("Patrol")]
        public float patrolSpeed = 1.5f;
        public float patrolPointReachDistance = 0.5f;

        [Header("Hook Investigation")]
        public float swimToHookSpeed = 3f;
        public float stoppingDistance = 0.25f;

        [Range(0f, 1f)]
        public float biteChance = 1f;

        public float minReactionDelay = 0f;
        public float maxReactionDelay = 0f;

        private Vector3 patrolTarget;
        private bool isInsideFishArea;

        private bool movingToHook;
        private bool isHooked;
        private Transform hookTarget;

        private Coroutine investigateCoroutine;


        private void Awake()
        {
            fishRb = GetComponent<Rigidbody>();
        }
        private void Start()
        {
            if (fishArea == null)
            {
                fishArea = FindFirstObjectByType<FishArea>();
            }

            fishArea.RegisterFish(this);

            PickNewPatrolTarget();

            StartCoroutine(PatrolTargetRoutine());
        }

        private void OnDestroy()
        {
            if (fishArea != null)
            {
                fishArea.UnregisterFish(this);
            }
        }

        private void Update()
        {
            if (!isInsideFishArea)
                return;

            if (isHooked)
                return;

            if (movingToHook)
            {
                SwimTowardsHook();
            }
            else
            {
                Patrol();
            }
        }

        private void Patrol()
        {
            Vector3 direction =
                patrolTarget - transform.position;

            if (direction.magnitude <= patrolPointReachDistance)
            {
                PickNewPatrolTarget();
                return;
            }

            transform.position +=
                direction.normalized *
                patrolSpeed *
                Time.deltaTime;

            RotateTowards(direction);
        }

        private void SwimTowardsHook()
        {
            if (hookTarget == null)
            {
                movingToHook = false;
                return;
            }

            Vector3 direction =
                hookTarget.position - transform.position;

            float distance = direction.magnitude;

            if (distance <= stoppingDistance)
            {
                movingToHook = false;
                isHooked = true;

                // if (rb != null)
                // {
                //     rb.useGravity = true;
                // }
                fishArea.hookedRb.useGravity = true;

                UpdateGravity();

                Debug.Log(name + " reached the hook and is now hooked.");

                return;
            }

            transform.position +=
                direction.normalized *
                swimToHookSpeed *
                Time.deltaTime;

            RotateTowards(direction);
        }

        private void RotateTowards(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.001f)
                return;

            Quaternion targetRotation =
                Quaternion.LookRotation(direction);

            transform.rotation =
                Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    5f * Time.deltaTime);
        }

        private IEnumerator PatrolTargetRoutine()
        {
            while (true)
            {
                if (!movingToHook && !isHooked)
                {
                    PickNewPatrolTarget();
                }

                yield return new WaitForSeconds(
                    Random.Range(3f, 8f));
            }
        }

        private void PickNewPatrolTarget()
        {
            Bounds bounds = fishArea.GetBounds();

            patrolTarget = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                Random.Range(bounds.min.y, bounds.max.y),
                Random.Range(bounds.min.z, bounds.max.z));
        }

        public void InvestigateHook(Transform hook)
        {
            Debug.Log(name + " InvestigateHook called");

            if (movingToHook || isHooked)
                return;

            if (Random.value > biteChance)
                return;

            investigateCoroutine =
                StartCoroutine(InvestigateAfterDelay(hook));
        }

        private IEnumerator InvestigateAfterDelay(Transform hook)
        {
            Debug.Log(name + " starting investigation");

            float delay =
                Random.Range(
                    minReactionDelay,
                    maxReactionDelay);

            yield return new WaitForSeconds(delay);

            hookTarget = hook;

            movingToHook = true;

            investigateCoroutine = null;

            Debug.Log(name + " moving to hook");
        }

        /// <summary>
        /// Call this when the hook should no longer be pursued — e.g. it
        /// left the water before this fish reached it. Cancels the
        /// investigation (whether still in its reaction delay or already
        /// swimming) and lets the fish resume patrolling. Does nothing if
        /// the fish has already reached the hook (isHooked).
        /// </summary>
        public void CancelHookInvestigation()
        {
            if (isHooked)
                return;

            if (investigateCoroutine != null)
            {
                StopCoroutine(investigateCoroutine);
                investigateCoroutine = null;
            }

            if (movingToHook)
            {
                movingToHook = false;
                hookTarget = null;

                Debug.Log(name + " hook left, returning to patrol");
            }
        }

        public void SetInsideFishArea(bool inside)
        {
            isInsideFishArea = inside;

            Debug.Log($"{name} inside fish area = {inside}");

            UpdateGravity();
        }

        /// <summary>
        /// Central authority for this fish's gravity state.
        /// Rules:
        /// - Outside the fish area: gravity ON (fish falls/behaves normally).
        /// - Inside the fish area, not hooked: gravity OFF (fish "swims" via
        ///   scripted movement).
        /// - Inside the fish area AND hooked: gravity ON, so the hooked fish
        ///   is pulled by the line/rig physics instead of floating in place.
        /// Called whenever isInsideFishArea or isHooked changes.
        /// </summary>
        private void UpdateGravity()
        {
            if (fishRb == null)
                return;

            fishRb.useGravity = isHooked || !isInsideFishArea;
        }
    }
}