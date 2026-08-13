using System.Collections;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Owns where we are in the deck. It knows nothing about slide types, backgrounds or input — it
    /// sequences a transition around a renderer swap and announces the result.
    ///
    /// The navigation lock is the important detail: everything is refused while a transition is
    /// in flight, which is what makes holding D step one slide at a time instead of skipping three.
    /// </summary>
    public sealed class PresentationDirector : MonoBehaviour, IPresentationDirector
    {
        [Header("Deck")]
        [SerializeField] private PresentationDeckSO _deck;

        [Header("Rig")]
        [SerializeField] private SlideRendererRegistry _renderers;
        [SerializeField] private SlideTransitionPlayer _transitions;
        [SerializeField] private BackdropController _backdrop;

        [Header("Startup")]
        [SerializeField, Tooltip("Show the first slide automatically on Start.")]
        private bool _showFirstSlideOnStart = true;

        private IEventService _events;
        private Coroutine _move;

        public int Index { get; private set; } = -1;
        public int Count => _deck != null ? _deck.Count : 0;
        public SlideSO Current => _deck != null ? _deck.SlideAt(Index) : null;
        public bool IsTransitioning => _move != null;

        /// <summary>
        /// Optional — the deck runs without a container. When present, slide changes are announced on
        /// the shared event hub so the HUD and later the stage loader can react without a reference
        /// back to the director.
        /// </summary>
        [Inject]
        private void Inject(IEventService events) => _events = events;

        private void Awake()
        {
            if (_renderers == null) _renderers = GetComponentInChildren<SlideRendererRegistry>(true);
            if (_transitions == null) _transitions = GetComponentInChildren<SlideTransitionPlayer>(true);
            if (_backdrop == null) _backdrop = GetComponentInChildren<BackdropController>(true);

            if (_deck == null)
                Debug.LogWarning("[PresentationDirector] No deck assigned — nothing to present.", this);
            else if (_deck.Count == 0)
                Debug.LogWarning("[PresentationDirector] Deck is empty.", this);
        }

        private void Start()
        {
            if (_showFirstSlideOnStart && Count > 0) GoTo(0, false);
        }

        public void Next()
        {
            if (Index + 1 < Count) GoTo(Index + 1, false);
        }

        public void Previous()
        {
            if (Index - 1 >= 0) GoTo(Index - 1, true);
        }

        public void Restart()
        {
            if (Count > 0) GoTo(0, true);
        }

        /// <summary>
        /// Jump to the last slide. Marked as travelling BACKWARDS deliberately: it is a jump, not an
        /// advance, and the backwards flag is what the transition and the HUD read to decide direction.
        /// </summary>
        public void GoToEnd()
        {
            if (Count > 0 && Index != Count - 1) GoTo(Count - 1, true);
        }

        public void JumpToSection(int sectionNumber)
        {
            if (_deck == null) return;

            int target = _deck.FirstSlideOfSection(sectionNumber);
            // -1 rather than a clamp: a mistyped number key during a talk should do nothing.
            if (target < 0) return;
            if (target == Index) return;

            GoTo(target, target < Index);
        }

        private void GoTo(int index, bool backwards)
        {
            if (_deck == null || index < 0 || index >= Count) return;
            if (IsTransitioning) return;

            _move = StartCoroutine(MoveRoutine(index, backwards));
        }

        private IEnumerator MoveRoutine(int index, bool backwards)
        {
            SlideSO slide = _deck.SlideAt(index);
            // Resolved from the MOVE, not just the destination: the deck returns a cut when both ends are
            // angles of the same place, so the Cinemachine blend is not hidden behind a dissolve.
            SlideTransitionSO transition = _deck.ResolveTransition(Current, slide);

            // First show of the deck: no out-half, there is nothing on screen to cover.
            bool isFirstShow = Index < 0;

            if (!isFirstShow && _transitions != null)
                yield return _transitions.PlayOut(transition, slide);

            // --- everything below happens while the screen is covered ---
            Index = index;

            if (_backdrop != null)
            {
                // A scene slide needs the backdrop gone, not repainted — see SlideSO.WantsBackdrop.
                if (slide != null && slide.WantsBackdrop) _backdrop.Apply(_deck.ResolveBackground(slide));
                else _backdrop.Hide();
            }

            if (_renderers != null && !_renderers.Show(slide, _deck))
                Debug.LogWarning($"[PresentationDirector] slide {index} ('{slide?.name}') did not render.", this);

            _events?.Publish(new SlideChangedEvent(index, Count, slide, backwards));
            // --- end of covered window ---

            if (!isFirstShow && _transitions != null)
                yield return _transitions.PlayIn(transition);

            _move = null;
        }
    }
}
