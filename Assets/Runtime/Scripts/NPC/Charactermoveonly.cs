using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Walks a character through an ordered list of waypoints, swapping to a Walk animator
    /// controller for the whole route, then switching to a Talk animator controller once it
    /// reaches the FINAL waypoint. Does NOT play any voice/dialogue itself - that's owned by
    /// whatever triggers <see cref="Move"/> (e.g. a NarrativeTriggerZone-adjacent trigger, after
    /// its dialogue finishes).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class CharacterMoveOnly : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Animator _animator;

        [Header("Animator Controllers")]
        [Tooltip("Controller to switch to while walking through the waypoints.")]
        [SerializeField] private RuntimeAnimatorController _walkController;
        [Tooltip("Controller to switch to once the character reaches the FINAL waypoint.")]
        [SerializeField] private RuntimeAnimatorController _talkController;

        [Header("Movement")]
        [Tooltip("Ordered list of points to walk through. The character walks to each in order; " +
                 "the Talk controller only plays once it reaches the LAST one in this list.")]
        [SerializeField] private List<Transform> _waypoints = new List<Transform>();
        [SerializeField] private float _moveSpeed = 2f;
        [SerializeField] private float _arrivalThreshold = 0.05f;
        [SerializeField] private bool _rotateTowardsTarget = true;
        [SerializeField] private float _rotateSpeed = 10f;

        [Header("Look At (on arrival)")]
        [Tooltip("Once the character reaches the final waypoint, it snaps to face this transform " +
                 "(Y-axis only, stays upright). Leave empty to skip.")]
        [SerializeField] private Transform _lookAtTarget;

        public event Action MoveStarted;
        /// <summary>Fired every time a waypoint is reached, including intermediate ones.</summary>
        public event Action<int> WaypointReached;
        /// <summary>Fired once the FINAL waypoint is reached.</summary>
        public event Action Arrived;

        private Coroutine _moveRoutine;

        private void Reset()
        {
            _animator = GetComponent<Animator>();
        }

        /// <summary>
        /// Call this to start walking through the waypoint list in order. Safe to call multiple
        /// times - a call while already moving is ignored.
        /// </summary>
        public void Move()
        {
            if (_moveRoutine != null)
            {
                Debug.LogWarning($"[{name}] Already moving, ignoring Move() call.", this);
                return;
            }

            if (_waypoints == null || _waypoints.Count == 0)
            {
                Debug.LogWarning($"[{name}] No waypoints assigned, nothing to move to.", this);
                return;
            }

            _moveRoutine = StartCoroutine(MoveRoutine());
        }

        public void Stop()
        {
            if (_moveRoutine != null)
            {
                StopCoroutine(_moveRoutine);
                _moveRoutine = null;
            }
        }

        private IEnumerator MoveRoutine()
        {
            MoveStarted?.Invoke();

            SwitchController(_walkController);

            for (int i = 0; i < _waypoints.Count; i++)
            {
                Transform waypoint = _waypoints[i];
                if (waypoint == null)
                {
                    Debug.LogWarning($"[{name}] Waypoint at index {i} is null, skipping.", this);
                    continue;
                }

                yield return MoveToPoint(waypoint);
                WaypointReached?.Invoke(i);
            }

            FaceLookAtTarget();

            SwitchController(_talkController);
            Arrived?.Invoke();

            _moveRoutine = null;
        }

        /// <summary>
        /// Snaps the character to face _lookAtTarget on the Y axis only (stays upright), called
        /// once after the final waypoint is reached. No-op if no target is assigned.
        /// </summary>
        private void FaceLookAtTarget()
        {
            if (_lookAtTarget == null) return;

            Vector3 toTarget = _lookAtTarget.position - transform.position;
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