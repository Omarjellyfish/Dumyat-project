using UnityEngine;
using System.Collections;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>What holds the father at a point before he walks on.</summary>
    public enum PathGate
    {
        /// <summary>Nothing — walk straight on to the next point.</summary>
        None = 0,

        /// <summary>Wait until a dialogue finishes. Someone else has to start it; this only listens.</summary>
        DialogueEnds = 1,

        /// <summary>Wait until the player has the fishing rod in hand.</summary>
        RodPickedUp = 2,

        /// <summary>Wait a fixed number of seconds.</summary>
        Seconds = 3
    }

    /// <summary>
    /// Walks the father along a hand-placed path, pausing at each point until that point's gate opens.
    ///
    /// Every gate carries a maximum wait, and that is not belt-and-braces — it is the only thing that
    /// makes a second visit work. The narrative sequences here are once-per-save, so a returning player
    /// hears nothing, and a gate waiting on <see cref="PathGate.DialogueEnds"/> would sit forever
    /// waiting for a line that has already been spoken. The father would never leave the beach.
    /// </summary>
    public class FatherPathMover : MonoBehaviour
    {
        [System.Serializable]
        public struct PathPoint
        {
            public Transform point;

            [Tooltip("Movement speed used while traveling TO this point.")]
            public float moveSpeed;

            [Tooltip("What holds him here once he arrives, before he walks on.")]
            public PathGate waitFor;

            [Tooltip("Which sequence opens a Dialogue Ends gate. Leave empty to accept ANY dialogue " +
                     "ending — only do that where nothing else can possibly be talking.")]
            public DialogueSequenceSO waitForDialogue;

            [Tooltip("Seconds to wait when Wait For is Seconds.")]
            public float waitTimeAtPoint;

            [Tooltip("Safety valve: give up on Wait For after this many seconds and walk on anyway. " +
                     "0 uses the mover's default. Set it on any gate whose trigger can legitimately " +
                     "never fire — a dialogue that has already been heard, a rod the player ignores.")]
            public float maxWaitSeconds;
        }

        [Header("Path")]
        [SerializeField] private PathPoint[] pathPoints;

        [Header("Before Setting Off")]
        [Tooltip("What holds him in place at the start, while he greets the player. The intro sequence " +
                 "is published by the arrival zone, NOT by this script — this only waits for it.")]
        [SerializeField] private PathGate introGate = PathGate.DialogueEnds;

        [Tooltip("Which sequence opens the intro gate. Leave empty to accept any dialogue ending.")]
        [SerializeField] private DialogueSequenceSO introDialogue;

        [SerializeField] private float introMaxWaitSeconds = 45f;

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

        [Tooltip("Default safety valve for any gate that does not set its own.")]
        [SerializeField] private float defaultMaxWaitSeconds = 120f;

        private IEventService _eventService;
        private Coroutine pathRoutine;
        private bool _gateOpen;
        private DialogueSequenceSO _expectedDialogue;

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
                Debug.LogWarning($"{name}: FatherPathMover has no path points assigned.", this);
                return;
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

            UnsubscribeGates();
            SetWalking(false);
            SetTalking(false);
        }

        private void OnDisable() => UnsubscribeGates();

        private IEnumerator RunPath()
        {
            // Intro: father talks, does not move. The zone the player lands in publishes the sequence —
            // this script only waits for it.
            //
            // It used to publish the intro itself, which made two owners of one sequence: the arrival
            // zone fired it as well, so a player who reached the zone late (teleporting off the boat is
            // what does it) heard the father restart from his first line. A trigger-once zone cannot
            // dedupe against a second publisher — so there is only one publisher now.
            SetWalking(false);
            SetTalking(true);
            yield return WaitForGate(introGate, introDialogue, 0f, introMaxWaitSeconds);
            SetTalking(false);

            do
            {
                for (int i = 0; i < pathPoints.Length; i++)
                {
                    PathPoint current = pathPoints[i];

                    if (current.point == null)
                    {
                        Debug.LogWarning($"{name}: Path point at index {i} is null, skipping.", this);
                        continue;
                    }

                    SetWalking(true);
                    yield return MoveToPoint(current);
                    SetWalking(false);
                    SetTalking(false);

                    yield return WaitForGate(current.waitFor, current.waitForDialogue,
                                             current.waitTimeAtPoint, current.maxWaitSeconds);
                }

                if (!loopPath)
                {
                    SetWalking(false);
                    SetTalking(false);

                    // Whoever needs to know he has arrived — the jetty board, above all — subscribes.
                    // He does not hold a reference to it: he used to hold the teleport interactable
                    // directly and switch it on, which meant the father knew how the player boards a
                    // boat.
                    _eventService?.Publish(new FatherPathCompletedEvent());
                }
            }
            while (loopPath);

            pathRoutine = null;
        }

        /// <summary>
        /// Holds until the gate opens or its safety valve expires. Never blocks forever.
        /// </summary>
        private IEnumerator WaitForGate(PathGate gate, DialogueSequenceSO expected, float seconds, float maxWaitSeconds)
        {
            switch (gate)
            {
                case PathGate.None:
                    yield break;

                case PathGate.Seconds:
                    if (seconds > 0f) yield return new WaitForSeconds(seconds);
                    yield break;
            }

            float limit = maxWaitSeconds > 0f ? maxWaitSeconds : defaultMaxWaitSeconds;

            if (_eventService == null)
            {
                Debug.LogWarning($"{name}: no IEventService injected, so the '{gate}' gate can never " +
                                 $"open. Waiting {limit}s and walking on. Does this scene have a " +
                                 "ContainerScope?", this);
                yield return new WaitForSeconds(limit);
                yield break;
            }

            _gateOpen = false;
            _expectedDialogue = expected;
            Subscribe(gate);

            float elapsed = 0f;
            while (!_gateOpen && elapsed < limit)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Unsubscribe(gate);

            if (!_gateOpen)
            {
                // Expected on a repeat visit, where the line that would have opened this gate is
                // suppressed as already-heard. Logged rather than silent so a gate that never opens on
                // a FIRST visit is still findable.
                Debug.Log($"{name}: '{gate}' gate timed out after {limit}s — walking on.", this);
            }
        }

        // Subscriptions are held only for the duration of one gate — he is not listening for a rod
        // pickup while walking, and he is not listening for dialogue once he has set off.
        private void Subscribe(PathGate gate)
        {
            switch (gate)
            {
                case PathGate.DialogueEnds:
                    _eventService.Subscribe<DialogueEndedEvent>(OnDialogueEnded);
                    break;
                case PathGate.RodPickedUp:
                    _eventService.Subscribe<FishingRodPickedUpEvent>(OnRodPickedUp);
                    break;
            }
        }

        private void Unsubscribe(PathGate gate)
        {
            switch (gate)
            {
                case PathGate.DialogueEnds:
                    _eventService.Unsubscribe<DialogueEndedEvent>(OnDialogueEnded);
                    break;
                case PathGate.RodPickedUp:
                    _eventService.Unsubscribe<FishingRodPickedUpEvent>(OnRodPickedUp);
                    break;
            }
        }

        // Blanket cleanup for teardown, where the currently-open gate is not tracked. Unsubscribing an
        // absent handler is a no-op, so calling both is safe.
        private void UnsubscribeGates()
        {
            if (_eventService == null) return;
            _eventService.Unsubscribe<DialogueEndedEvent>(OnDialogueEnded);
            _eventService.Unsubscribe<FishingRodPickedUpEvent>(OnRodPickedUp);
        }

        // Only HIS line releases him. Before DialogueEndedEvent carried a sequence, any line ending
        // anywhere opened this gate — a fish remark on the far side of the beach set him walking
        // mid-speech. An unset expectation still accepts anything, for a scene where nothing else talks.
        private void OnDialogueEnded(DialogueEndedEvent evt)
        {
            if (_expectedDialogue != null && evt.Sequence != _expectedDialogue) return;
            _gateOpen = true;
        }

        private void OnRodPickedUp(FishingRodPickedUpEvent evt) => _gateOpen = true;

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
