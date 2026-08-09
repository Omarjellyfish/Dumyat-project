using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Moves this object back and forth between a list of points, forever:
    /// point 0 -> 1 -> 2 ... -> last -> back down to 0 -> repeat.
    /// No animation, no collider, no rigidbody -- pure transform movement.
    /// </summary>
    public class PatrolPingPong : MonoBehaviour
    {
        [SerializeField] private Transform[] points;
        [SerializeField] private float speed = 2f;
        [SerializeField] private float reachDistance = 0.1f;
        [SerializeField] private bool rotateTowardsTarget = true;
        [SerializeField] private float rotationSpeed = 5f;

        private int _targetIndex = 1;
        private int _direction = 1; // +1 moving forward through the list, -1 moving backward

        private void Update()
        {
            if (points == null || points.Length < 2) return;

            Transform target = points[_targetIndex];
            if (target == null) return;

            transform.position = Vector3.MoveTowards(transform.position, target.position, speed * Time.deltaTime);

            if (rotateTowardsTarget)
            {
                Vector3 dir = target.position - transform.position;
                if (dir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
                }
            }

            if (Vector3.Distance(transform.position, target.position) <= reachDistance)
            {
                AdvanceTarget();
            }
        }

        private void AdvanceTarget()
        {
            // Bounce back at either end of the list.
            if (_targetIndex + _direction < 0 || _targetIndex + _direction >= points.Length)
            {
                _direction *= -1;
            }

            _targetIndex += _direction;
        }
    }
}