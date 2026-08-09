using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Walks a character through one or more ordered "routes", each a list of waypoints. Every
    /// call to <see cref="Move"/> plays the NEXT unplayed route in order - so the same component
    /// can be reused for multiple separate walk legs (e.g. one triggered by an early zone, a
    /// second triggered later by a different zone), without needing a second component fighting
    /// over the same Animator.
    ///
    /// While walking, switches to the Walk animator controller. On reaching the LAST waypoint of
    /// the current route, optionally snaps to face that route's Look At target, then switches to
    /// the Talk animator controller. Does NOT play any voice/dialogue itself - that's owned by
    /// whatever triggers Move (e.g. a trigger zone, after its dialogue finishes).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class CharacterMoveOnly : MonoBehaviour
    {
        [Serializable]
        public class Route
        {
            [Tooltip("Ordered points to walk through for this route.")]
            public List<Transform> Waypoints = new List<Transform>();

            [Tooltip("Once the character reaches the final waypoint of THIS route, it snaps to face " +
                     "this transform (Y-axis only, stays upright). Leave empty to skip.")]
            public Transform LookAtTarget;
        }

        [Header("References")]
        [SerializeField] private Animator _animator;

        [Header("Animator Controllers")]
        [Tooltip("Controller to switch to while walking through a route's waypoints.")]
        [SerializeField] private RuntimeAnimatorController _walkController;
        [Tooltip("Controller to switch to once the character reaches the final waypoint of a route.")]
        [SerializeField] private RuntimeAnimatorController _talkController;

        [Header("Routes")]
        [Tooltip("Each call to Move() plays the next unplayed route in this list, in order. Add a " +
                 "new route for each separate walk leg this character should do (e.g. one per " +
                 "trigger zone).")]
        [SerializeField] private List<Route> _routes = new List<Route>();

        [Header("Movement")]
        [SerializeField] private float _moveSpeed = 2f;
        [SerializeField] private float _arrivalThreshold = 0.05f;
        [SerializeField] private bool _rotateTowardsTarget = true;
        [SerializeField] private float _rotateSpeed = 10f;

        public event Action MoveStarted;
        /// <summary>Fired every time a waypoint is reached, including intermediate ones.</summary>
        public event Action<int> WaypointReached;
        /// <summary>Fired once the final waypoint of a route is reached.</summary>
        public event Action Arrived;

        private Coroutine _moveRoutine;
        private int _nextRouteIndex;

        private void Reset()
        {
            _animator = GetComponent<Animator>();
        }

        /// <summary>
        /// Plays the next unplayed route in order. Safe to call multiple times - a call while
        /// already moving, or once all routes have been played, is ignored (logs a warning).
        /// </summary>
        public void Move()
        {
            if (_moveRoutine != null)
            {
                Debug.LogWarning($"[{name}] Already moving, ignoring Move() call.", this);
                return;
            }

            if (_routes == null || _nextRouteIndex >= _routes.Count)
            {
                Debug.LogWarning($"[{name}] No more routes to play (played {_nextRouteIndex}/{_routes?.Count ?? 0}).", this);
                return;
            }

            Route route = _routes[_nextRouteIndex];
            _nextRouteIndex++;

            if (route.Waypoints == null || route.Waypoints.Count == 0)
            {
                Debug.LogWarning($"[{name}] Route {_nextRouteIndex - 1} has no waypoints, skipping.", this);
                return;
            }

            _moveRoutine = StartCoroutine(MoveRoutine(route));
        }

        public void Stop()
        {
            if (_moveRoutine != null)
            {
                StopCoroutine(_moveRoutine);
                _moveRoutine = null;
            }
        }

        private IEnumerator MoveRoutine(Route route)
        {
            MoveStarted?.Invoke();

            SwitchController(_walkController);

            for (int i = 0; i < route.Waypoints.Count; i++)
            {
                Transform waypoint = route.Waypoints[i];
                if (waypoint == null)
                {
                    Debug.LogWarning($"[{name}] Waypoint at index {i} is null, skipping.", this);
                    continue;
                }

                yield return MoveToPoint(waypoint);
                WaypointReached?.Invoke(i);
            }

            FaceLookAtTarget(route.LookAtTarget);

            SwitchController(_talkController);
            Arrived?.Invoke();

            _moveRoutine = null;
        }

        /// <summary>
        /// Snaps the character to face the given target on the Y axis only (stays upright), called
        /// once after the final waypoint of a route is reached. No-op if target is null.
        /// </summary>
        private void FaceLookAtTarget(Transform lookAtTarget)
        {
            if (lookAtTarget == null) return;

            Vector3 toTarget = lookAtTarget.position - transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude <= 0.0001f) return;

            transform.rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        }

        private void SwitchController(RuntimeAnimatorController controller)
        {
            if (_animator == null)
            {
                Debug.LogError($"[{name}] Animator is not assigned.", this);
                return;
            }

            if (controller == null) return;

            if (_animator.runtimeAnimatorController != controller)
            {
                _animator.runtimeAnimatorController = controller;
            }
        }

        private IEnumerator MoveToPoint(Transform target)
        {
            while (true)
            {
                Vector3 currentPos = transform.position;
                Vector3 destination = target.position;

                Vector3 toTarget = destination - currentPos;
                toTarget.y = 0f;

                if (toTarget.sqrMagnitude <= _arrivalThreshold * _arrivalThreshold)
                {
                    break;
                }

                Vector3 direction = toTarget.normalized;

                if (_rotateTowardsTarget && direction.sqrMagnitude > 0f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, _rotateSpeed * Time.deltaTime);
                }

                transform.position = Vector3.MoveTowards(currentPos, new Vector3(destination.x, currentPos.y, destination.z), _moveSpeed * Time.deltaTime);

                yield return null;
            }
        }
    }
}