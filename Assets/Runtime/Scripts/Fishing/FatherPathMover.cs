using UnityEngine;
using System.Collections;

namespace DumyatRuntime
{
    public class FatherPathMover : MonoBehaviour
    {
        [System.Serializable]
        public struct PathPoint
        {
            public Transform point;

            [Tooltip("Movement speed used while traveling TO this point.")]
            public float moveSpeed;

            [Tooltip("How long to wait once this point is reached.")]
            public float waitTimeAtPoint;
        }

        [Header("Path")]
        [SerializeField] private PathPoint[] pathPoints;

        [Header("Timing")]
        [Tooltip("Delay before the father starts moving at all.")]
        [SerializeField] private float initialWaitTime = 2f;

        [Header("Movement")]
        [SerializeField] private float reachDistance = 0.1f;
        [SerializeField] private bool rotateTowardsTarget = true;
        [SerializeField] private float rotationSpeed = 5f;

        [Header("Behaviour")]
        [SerializeField] private bool loopPath = false;
        [SerializeField] private bool playOnStart = true;

        private Coroutine pathRoutine;
        private bool isMoving;

        private void Start()
        {
            if (playOnStart)
            {
                StartPath();
            }
        }

        public void StartPath()
        {
            if (pathPoints == null || pathPoints.Length == 0)
            {
                Debug.LogWarning($"{name}: FatherPathMover has no path points assigned.");
                return;
            }

            if (pathRoutine != null)
            {
                StopCoroutine(pathRoutine);
            }

            pathRoutine = StartCoroutine(RunPath());
        }

        public void StopPath()
        {
            if (pathRoutine != null)
            {
                StopCoroutine(pathRoutine);
                pathRoutine = null;
            }

            isMoving = false;
        }

        private IEnumerator RunPath()
        {
            isMoving = false;

            yield return new WaitForSeconds(initialWaitTime);

            do
            {
                for (int i = 0; i < pathPoints.Length; i++)
                {
                    PathPoint current = pathPoints[i];

                    if (current.point == null)
                    {
                        Debug.LogWarning($"{name}: Path point at index {i} is null, skipping.");
                        continue;
                    }

                    yield return MoveToPoint(current);

                    if (current.waitTimeAtPoint > 0f)
                    {
                        yield return new WaitForSeconds(current.waitTimeAtPoint);
                    }
                }
            }
            while (loopPath);

            isMoving = false;
            pathRoutine = null;
        }

        private IEnumerator MoveToPoint(PathPoint target)
        {
            isMoving = true;

            float speed = target.moveSpeed > 0f ? target.moveSpeed : 1f;

            while (Vector3.Distance(transform.position, target.point.position) > reachDistance)
            {
                Vector3 direction = target.point.position - transform.position;

                transform.position = Vector3.MoveTowards(
                    transform.position,
                    target.point.position,
                    speed * Time.deltaTime);

                if (rotateTowardsTarget && direction.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(direction);

                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        targetRotation,
                        rotationSpeed * Time.deltaTime);
                }

                yield return null;
            }

            transform.position = target.point.position;

            isMoving = false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (pathPoints == null) return;

            Gizmos.color = Color.yellow;

            Vector3 previous = transform.position;

            foreach (var p in pathPoints)
            {
                if (p.point == null) continue;

                Gizmos.DrawSphere(p.point.position, 0.15f);
                Gizmos.DrawLine(previous, p.point.position);
                previous = p.point.position;
            }
        }
#endif
    }
}