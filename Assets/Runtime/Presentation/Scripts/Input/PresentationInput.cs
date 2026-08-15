using Reflex.Attributes;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DumyatRuntime
{
    /// <summary>
    /// Turns keys into director calls. That is its whole job — it holds no deck state and makes no
    /// navigation decisions of its own, so the key map can be re-pointed without touching sequencing.
    ///
    /// Reads the new Input System when it is enabled and falls back to the legacy manager otherwise,
    /// because the project ships XR and its Active Input Handling is not this deck's business.
    /// </summary>
    public sealed class PresentationInput : MonoBehaviour
    {
        [Header("Rig")]
        [SerializeField] private PresentationDirector _director;
        [SerializeField] private SpeakerNotesOverlay _notes;
        [SerializeField] private SlideCounterHud _counter;
        [SerializeField, Tooltip("Pause target for P. Optional — the key does nothing without it.")]
        private MediaSlideRenderer _media;

        [SerializeField, Tooltip("Before/after audio target for Z and X. Optional.\n\nZ and X because " +
                                 "nearly everything else is spoken for: A/D and the arrows navigate, " +
                                 "Space advances, N is notes, C the counter, P pauses media, and 1–9 " +
                                 "jump between sections — so the obvious '1 and 2' would teleport the " +
                                 "presenter across the deck mid-demo.")]
        private AudioCompareRenderer _audio;

        [Header("Quit")]
        [SerializeField, Range(0f, 2f),
         Tooltip("Escape must be held this long to quit, so a stray press cannot end the talk.")]
        private float _quitHoldSeconds = 0.6f;

        private IPresentationDirector _injected;
        private float _escapeHeld;

        private IPresentationDirector Director =>
            _injected ?? _director as IPresentationDirector;

        [Inject]
        private void Inject(IPresentationDirector director) => _injected = director;

        private void Awake()
        {
            if (_director == null) _director = FindFirstObjectByType<PresentationDirector>();
            if (_director == null && _injected == null)
                Debug.LogWarning("[PresentationInput] No director found — keys will do nothing.", this);
        }

        private void Update()
        {
            IPresentationDirector director = Director;
            if (director == null) return;

            if (Pressed(KeyKind.Next))     director.Next();
            if (Pressed(KeyKind.Previous)) director.Previous();
            if (Pressed(KeyKind.Restart))  director.Restart();

            // The two ends of the talk. 0 and 9 rather than Home and End because they sit next to the
            // section digits, so the whole jump map is one row of keys.
            if (Pressed(KeyKind.First)) director.Restart();
            if (Pressed(KeyKind.Last))  director.GoToEnd();

            // SECTION JUMPS STOP AT 8, NOT 9. The 9 key is the end of the deck now, which costs section 9
            // (Future Plans) its direct key — and costs it nothing in practice, because the end of the deck
            // is the Thank You slide and Future Plans is the one immediately before it: press 9 then A.
            for (int section = 1; section <= 8; section++)
            {
                if (Pressed(KeyKind.Digit1 + (section - 1))) director.JumpToSection(section);
            }

            if (Pressed(KeyKind.Notes) && _notes != null) _notes.Toggle();
            if (Pressed(KeyKind.Counter) && _counter != null) _counter.Toggle();
            if (Pressed(KeyKind.PauseMedia) && _media != null) _media.TogglePause();
            if (Pressed(KeyKind.AudioBefore) && _audio != null) _audio.PlayBefore();
            if (Pressed(KeyKind.AudioAfter) && _audio != null) _audio.PlayAfter();

            HandleQuit();
        }

        private void HandleQuit()
        {
            if (Held(KeyKind.Quit))
            {
                _escapeHeld += Time.unscaledDeltaTime;
                if (_escapeHeld >= _quitHoldSeconds)
                {
                    _escapeHeld = 0f;
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                }
            }
            else _escapeHeld = 0f;
        }

        // Key identity is kept as this small enum rather than raw KeyCode/Key values so the two input
        // backends are the only place either enum is named.
        private enum KeyKind
        {
            // Digit1..Digit8 must stay contiguous and first — the section loop does arithmetic on them.
            Digit1 = 0, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8,
            First, Last,
            Next, Previous, Restart, Notes, Counter, PauseMedia, AudioBefore, AudioAfter, Quit,
        }

        private static bool Pressed(KeyKind kind)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                switch (kind)
                {
                    case KeyKind.Next:
                        return kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame ||
                               kb.spaceKey.wasPressedThisFrame || kb.pageDownKey.wasPressedThisFrame;
                    case KeyKind.Previous:
                        return kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame ||
                               kb.pageUpKey.wasPressedThisFrame;
                    case KeyKind.Restart: return kb.homeKey.wasPressedThisFrame;
                    // Both the digit row and the numpad, because a presenter clicker and a laptop keyboard
                    // do not agree on which one a "0" is.
                    case KeyKind.First:
                        return kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame;
                    case KeyKind.Last:
                        return kb.digit9Key.wasPressedThisFrame || kb.numpad9Key.wasPressedThisFrame ||
                               kb.endKey.wasPressedThisFrame;
                    case KeyKind.Notes:      return kb.nKey.wasPressedThisFrame;
                    case KeyKind.Counter:    return kb.cKey.wasPressedThisFrame;
                    case KeyKind.PauseMedia:  return kb.pKey.wasPressedThisFrame;
                    case KeyKind.AudioBefore: return kb.zKey.wasPressedThisFrame;
                    case KeyKind.AudioAfter:  return kb.xKey.wasPressedThisFrame;
                    default:
                        int digit = (int)kind - (int)KeyKind.Digit1;
                        if (digit < 0 || digit > 7) return false;
                        return kb[Key.Digit1 + digit].wasPressedThisFrame;
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            switch (kind)
            {
                case KeyKind.Next:
                    return Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow) ||
                           Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.PageDown);
                case KeyKind.Previous:
                    return Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow) ||
                           Input.GetKeyDown(KeyCode.PageUp);
                case KeyKind.Restart:    return Input.GetKeyDown(KeyCode.Home);
                case KeyKind.First:
                    return Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0);
                case KeyKind.Last:
                    return Input.GetKeyDown(KeyCode.Alpha9) || Input.GetKeyDown(KeyCode.Keypad9) ||
                           Input.GetKeyDown(KeyCode.End);
                case KeyKind.Notes:      return Input.GetKeyDown(KeyCode.N);
                case KeyKind.Counter:    return Input.GetKeyDown(KeyCode.C);
                case KeyKind.PauseMedia:  return Input.GetKeyDown(KeyCode.P);
                case KeyKind.AudioBefore: return Input.GetKeyDown(KeyCode.Z);
                case KeyKind.AudioAfter:  return Input.GetKeyDown(KeyCode.X);
                default:
                    int digit = (int)kind - (int)KeyKind.Digit1;
                    if (digit < 0 || digit > 7) return false;
                    return Input.GetKeyDown(KeyCode.Alpha1 + digit);
            }
#else
            return false;
#endif
        }

        private static bool Held(KeyKind kind)
        {
            if (kind != KeyKind.Quit) return false;
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null) return kb.escapeKey.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.Escape);
#else
            return false;
#endif
        }
    }
}
