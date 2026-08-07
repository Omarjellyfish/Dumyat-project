using UnityEngine;
using System.Collections;
using Reflex.Attributes;

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

            [Tooltip("How long to wait once this point is reached. Not used for point 0 — that wait is driven entirely by First Point Dialogue.")]
            public float waitTimeAtPoint;
        }

        [Header("Path")]
        [SerializeField] private PathPoint[] pathPoints;

        [Header("Intro Dialogue (father talks here, no movement, until this finishes)")]
        [SerializeField] private DialogueSequenceSO introDialogue;
        [SerializeField] private DialoguePriority introDialoguePriority = DialoguePriority.Normal;

        [Header("First Point Dialogue (father stays at point 0 and talks until this finishes — " +
                 "triggered externally, e.g. by the player, NOT by this script)")]
        [SerializeField] private DialogueSequenceSO firstPointDialogue;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private string isWalkingParam = "isWalking";
        [SerializeField] private string isTalkingParam = "isTalking";
        private int isWalkingHash;
        private int isTalkingHash;

        [Header("Movement")]
        [SerializeField] private float reachDistance = 0.1f;
        [SerializeField] private bool rotateTowardsTarget = true;
        [SerializeField] private float rotationSpeed = 5f;

        [Header("Behaviour")]
        [SerializeField] private bool loopPath = false;
        [SerializeField] private bool playOnStart = true;

        [Header("Fade Out")]
        [SerializeField] private FadeInBoat fadeInBoat;

        [Header("Block Buttons")]
        [SerializeField] private BlockTeleportButtons blockTeleportButtons;

        private IEventService _eventService;
        private Coroutine pathRoutine;
        private bool waitingForDialogue;

        [Inject]
        private void Inject(IEventService eventService) => _eventService = eventService;

        private void Awake()
        {
            isWalkingHash = Animator.StringToHash(isWalkingParam);
            isTalkingHash = Animator.StringToHash(isTalkingParam);

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }

        private void OnEnable()
        {
            _eventService?.Subscribe<DialogueEndedEvent>(OnDialogueEnded);
        }

        private void OnDisable()
        {
            _eventService?.Unsubscribe<DialogueEndedEvent>(OnDialogueEnded);
        }

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

            if (introDialogue == null)
            {
                Debug.LogWarning($"{name}: introDialogue is not assigned. The father will not move until one is set.");
            }

            if (firstPointDialogue == null)
            {
                Debug.LogWarning($"{name}: firstPointDialogue is not assigned. The father will not leave point 0 until one is set.");
            }

            if (pathRoutine != null)
            {
                StopCoroutine(pathRoutine);
            }

            SetWalking(false);
            SetTalking(false);
            pathRoutine = StartCoroutine(RunPath());
        }

        public void StopPath()
        {
            if (pathRoutine != null)
            {
                StopCoroutine(pathRoutine);
                pathRoutine = null;
            }

            waitingForDialogue = false;
            SetWalking(false);
            SetTalking(false);
        }

        private IEnumerator RunPath()
        {
            // Intro: father talks, does not move.
            SetWalking(false);
            SetTalking(true);
            yield return PlayAndWaitForDialogue(introDialogue, introDialoguePriority);
            SetTalking(false);

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

                    SetWalking(true);
                    yield return MoveToPoint(current);
                    SetWalking(false);

                    if (i == 0)
                    {
                        // Point 0: father stays put and idles while waiting
                        // (e.g. for the player to select the rod / trigger firstPointDialogue).
                        // This script does NOT publish firstPointDialogue itself; it just
                        // waits for whoever triggers it to finish.
                        SetTalking(false);
                        yield return WaitForExternalDialogue(firstPointDialogue);
                        SetTalking(false);
                    }
                    else if (current.waitTimeAtPoint > 0f)
                    {
                        // Idle while waiting at this point.
                        SetTalking(false);
                        yield return new WaitForSeconds(current.waitTimeAtPoint);
                    }
                }

                if (!loopPath)
                {
                    // Reached the final point: back to idle, then trigger the transition.
                    SetWalking(false);
                    SetTalking(false);
                    fadeInBoat.StartTransition();
                    blockTeleportButtons.UnblockButtons();

                }
            }
            while (loopPath);

            pathRoutine = null;
        }

        /// <summary>
        /// Publishes the given dialogue and waits for it to end. Used for
        /// dialogue this script itself is responsible for starting (intro).
        /// </summary>
        private IEnumerator PlayAndWaitForDialogue(DialogueSequenceSO sequence, DialoguePriority priority)
        {
            if (sequence == null)
            {
                yield break;
            }

            waitingForDialogue = true;

            _eventService.Publish(new PlayDialogueEvent(sequence, priority));

            yield return new WaitUntil(() => !waitingForDialogue);
        }

        /// <summary>
        /// Waits for the given dialogue to end WITHOUT starting it — used
        /// when some other system (e.g. the player) is responsible for
        /// triggering it, and this script only needs to know when it's done.
        /// </summary>
        private IEnumerator WaitForExternalDialogue(DialogueSequenceSO sequence)
        {
            if (sequence == null)
            {
                yield break;
            }

            waitingForDialogue = true;

            yield return new WaitUntil(() => !waitingForDialogue);
        }

        private void OnDialogueEnded(DialogueEndedEvent evt)
        {
            waitingForDialogue = false;
        }

        private void SetWalking(bool isWalking)
        {
            if (animator != null)
            {
                animator.SetBool(isWalkingHash, isWalking);
            }
        }

        private void SetTalking(bool isTalking)
        {
            if (animator != null)
            {
                animator.SetBool(isTalkingHash, isTalking);
            }
        }

        private IEnumerator MoveToPoint(PathPoint target)
        {
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