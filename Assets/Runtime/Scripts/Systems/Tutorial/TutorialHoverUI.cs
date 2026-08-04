using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;
using Reflex.Attributes;

namespace DumyatRuntime
{
    /// <summary>
    /// World-space UI Toolkit panel that displays tutorial step instructions.
    /// Attaches to a <see cref="UIDocument"/> configured for world-space rendering.
    /// The <see cref="TutorialRunner"/> drives this component per-step.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TutorialHoverUI : MonoBehaviour
    {
        // The card's own chrome, in both languages. These are not step content, so they live here
        // rather than in TutorialStepSO — every step would otherwise carry the same six strings.
        // Authored uppercase because UI Toolkit has no text-transform (a no-op on Arabic).
        private const string EyebrowEnglish = "TUTORIAL";
        private const string EyebrowArabic = "إرشادات";
        private const string SkipEnglish = "SKIP";
        private const string SkipArabic = "تخطّي";
        private const string NextEnglish = "NEXT";
        private const string NextArabic = "التالي";

        private const string ArabicClass = "tutorial-arabic";
        private const string InstructionArabicClass = "instruction-text--arabic";

        [Header("Animation")]
        [SerializeField, Range(0.05f, 0.5f)] private float _animationDuration = 0.2f;

        [Header("Positioning")]
        [Tooltip("Closest the card may sit to the player's eyes (m). Below roughly 0.5 a world-space " +
                 "panel is uncomfortable to focus on and clips the near plane on some headsets.")]
        [SerializeField, Min(0.3f)] private float _minDistance = 0.65f;

        [Tooltip("Furthest the card may sit from the player (m). Past this the type stops being " +
                 "readable, so the card is pulled in along the line to its anchor rather than " +
                 "staying glued to a distant object.")]
        [SerializeField, Min(0.5f)] private float _maxDistance = 1.5f;

        [Tooltip("Gap (m) kept between the card and any geometry between it and the player. When " +
                 "something occludes the anchor, the card slides toward the player to stay visible.")]
        [SerializeField, Min(0f)] private float _occlusionPadding = 0.2f;

        [Tooltip("How quickly the card eases to its target position. Lower is lazier.")]
        [SerializeField, Min(0.01f)] private float _positionLerpSpeed = 8f;

        [Tooltip("Layers that can occlude the card. Leave as Default to test everything solid.")]
        [SerializeField] private LayerMask _occluderMask = ~0;

        private UIDocument _uiDocument;
        private VisualElement _root;
        private VisualElement _panel;
        private Label _instructionLabel;
        private Label _eyebrowLabel;
        private Label _stepCounterLabel;
        private Label _stepTotalLabel;
        private VisualElement _progressDotsContainer;
        private VisualElement _iconContainer;
        private Button _skipButton;
        private Button _nextButton;

        private ITutorialService _tutorialService;
        private ISettingsService _settings;
        private IEventService _eventService;
        private Transform _currentAnchor;
        private Vector3 _currentOffset;
        private Coroutine _animationCoroutine;
        private bool _isVisible;

        // The step currently on the card, kept so a mid-step language change can re-render it
        // instead of leaving the player reading the old language until the step advances.
        private TutorialStepSO _currentStep;
        private int _currentTotalSteps;

        private VideoPlayer _videoPlayer;
        private RenderTexture _videoTexture;

        [Inject]
        private void Inject(ITutorialService tutorialService, ISettingsService settings, IEventService eventService)
        {
            _tutorialService = tutorialService;
            _settings = settings;
            _eventService = eventService;
        }

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();

            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.isLooping = true;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        }

        private void OnEnable()
        {
            // LanguageChangedEvent, not SubtitleModeChangedEvent: the card follows the interface
            // language, which now moves independently of whether merchant captions are on.
            // Subscribed before the document check — a card with a broken document has nothing to
            // draw, but leaving subscription state inconsistent between enables is worse.
            _eventService?.Subscribe<LanguageChangedEvent>(HandleLanguageChanged);

            if (_uiDocument == null || _uiDocument.rootVisualElement == null) return;

            _root = _uiDocument.rootVisualElement;
            CacheElements();
            SetupButtons();
            ApplyLanguage();

            // Start hidden
            _panel?.AddToClassList("tutorial-panel--hidden");
        }

        private void CacheElements()
        {
            _panel = _root.Q<VisualElement>("TutorialPanel");
            _instructionLabel = _root.Q<Label>("InstructionText");
            _eyebrowLabel = _root.Q<Label>("TutorialEyebrow");
            _stepCounterLabel = _root.Q<Label>("StepCounter");
            _stepTotalLabel = _root.Q<Label>("StepTotal");
            _progressDotsContainer = _root.Q<VisualElement>("ProgressDots");
            _iconContainer = _root.Q<VisualElement>("IconContainer");
            _skipButton = _root.Q<Button>("SkipButton");
            _nextButton = _root.Q<Button>("NextButton");
        }

        private void SetupButtons()
        {
            if (_skipButton != null)
            {
                _skipButton.clicked += OnSkipClicked;
            }
            if (_nextButton != null)
            {
                _nextButton.clicked += OnNextClicked;
            }
        }

        private void OnSkipClicked()
        {
            _tutorialService?.SkipTutorial();
        }

        private void OnNextClicked()
        {
            _tutorialService?.SkipCurrentStep();
        }

        /// <summary>
        /// Display a tutorial step on the hover panel.
        /// </summary>
        public void ShowStep(TutorialStepSO step, int stepIndex, int totalSteps, Transform anchor, Vector3 offset)
        {
            // Update anchor tracking
            _currentAnchor = anchor;
            _currentOffset = offset;

            // Position immediately if we have an anchor
            if (anchor != null)
            {
                transform.position = CalculateSafePosition();
            }

            // Held so a language change part-way through a step can redraw this same card.
            _currentStep = step;
            _currentTotalSteps = totalSteps;

            // The instruction, the eyebrow, the buttons and the "OF 07" caption are all
            // language-dependent, so they are written in one place.
            ApplyLanguage();

            ApplySkippability(step);

            // The step number is the one string that never translates. Zero-padded so the numeral
            // does not change width between steps.
            if (_stepCounterLabel != null)
            {
                _stepCounterLabel.text = (stepIndex + 1).ToString("00");
            }

            // Update progress dots
            UpdateProgressDots(stepIndex, totalSteps);

            // Update media
            UpdateMedia(step.Icon, step.Video);

            // Animate in
            if (!_isVisible)
            {
                Show();
            }
            else
            {
                // Already visible — play a subtle pulse to indicate step change
                PlayStepTransition();
            }
        }

        /// <summary>
        /// Shows or hides the two ways out of a step — Skip (abandons the tutorial) and Next (steps
        /// over it) — according to <see cref="TutorialStepSO.Skippable"/>.
        ///
        /// Hidden rather than merely disabled: a greyed-out button in VR invites the player to keep
        /// pointing at it and wondering what is broken, whereas a card with no exit reads as "this
        /// one you actually have to do". <see cref="TutorialService"/> refuses the calls as well, so
        /// nothing depends on the card getting this right.
        /// </summary>
        private void ApplySkippability(TutorialStepSO step)
        {
            bool skippable = step == null || step.Skippable;

            if (_skipButton != null)
                _skipButton.style.display = skippable ? DisplayStyle.Flex : DisplayStyle.None;

            if (_nextButton != null)
                _nextButton.style.display = skippable ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Show the panel with a scale animation.</summary>
        public void Show()
        {
            _isVisible = true;
            gameObject.SetActive(true);

            if (_panel != null)
            {
                _panel.RemoveFromClassList("tutorial-panel--hidden");
                _panel.AddToClassList("tutorial-panel--visible");
            }
        }

        /// <summary>Hide the panel with a scale animation.</summary>
        public void Hide()
        {
            _isVisible = false;

            if (_panel != null)
            {
                _panel.RemoveFromClassList("tutorial-panel--visible");
                _panel.AddToClassList("tutorial-panel--hidden");
            }

            _currentAnchor = null;

            // Delay deactivation to allow animation to play
            if (_animationCoroutine != null)
            {
                StopCoroutine(_animationCoroutine);
            }
            _animationCoroutine = StartCoroutine(DeactivateAfterDelay(_animationDuration));
        }

        private IEnumerator DeactivateAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            gameObject.SetActive(false);
            _animationCoroutine = null;
        }

        // ─────────────────────────────────────────────────────────────
        // Language
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-renders every language-dependent string on the card from the player's current subtitle
        /// setting. Cheap and idempotent, so it is safe to call on every step and on every change.
        /// </summary>
        private void ApplyLanguage()
        {
            bool arabic = IsArabic;

            if (_instructionLabel != null)
            {
                _instructionLabel.text = ResolveInstruction(_currentStep, arabic);
                _instructionLabel.EnableInClassList(ArabicClass, arabic);
                _instructionLabel.EnableInClassList(InstructionArabicClass, arabic);
            }

            if (_eyebrowLabel != null)
            {
                _eyebrowLabel.text = arabic ? EyebrowArabic : EyebrowEnglish;
                _eyebrowLabel.EnableInClassList(ArabicClass, arabic);
            }

            if (_stepTotalLabel != null)
            {
                _stepTotalLabel.text = arabic ? $"من {_currentTotalSteps:00}" : $"OF {_currentTotalSteps:00}";
                _stepTotalLabel.EnableInClassList(ArabicClass, arabic);
            }

            if (_skipButton != null)
            {
                _skipButton.text = arabic ? SkipArabic : SkipEnglish;
                _skipButton.EnableInClassList(ArabicClass, arabic);
            }

            if (_nextButton != null)
            {
                _nextButton.text = arabic ? NextArabic : NextEnglish;
                _nextButton.EnableInClassList(ArabicClass, arabic);
            }
        }

        /// <summary>
        /// Whether the card should draw in Arabic. Follows the interface language, which is always a
        /// real language — turning merchant captions off no longer drags the tutorial back to
        /// English. A scene without a ContainerScope degrades to English.
        /// </summary>
        private bool IsArabic => UiLanguage.IsArabic(_settings);

        private static string ResolveInstruction(TutorialStepSO step, bool arabic)
        {
            if (step == null) return string.Empty;
            if (!arabic) return step.InstructionText;

            // Arabic is authored per step, so a newly added step can legitimately land without one.
            // Falling back to English beats an empty card mid-tutorial.
            return string.IsNullOrWhiteSpace(step.InstructionTextArabic)
                ? step.InstructionText
                : step.InstructionTextArabic;
        }

        private void HandleLanguageChanged(LanguageChangedEvent evt) => ApplyLanguage();

        private void UpdateProgressDots(int currentStep, int totalSteps)
        {
            if (_progressDotsContainer == null) return;

            _progressDotsContainer.Clear();

            for (int i = 0; i < totalSteps; i++)
            {
                var dot = new VisualElement();
                dot.AddToClassList("progress-dot");

                if (i < currentStep)
                {
                    dot.AddToClassList("progress-dot--completed");
                }
                else if (i == currentStep)
                {
                    dot.AddToClassList("progress-dot--active");
                }
                else
                {
                    dot.AddToClassList("progress-dot--inactive");
                }

                _progressDotsContainer.Add(dot);
            }
        }

        private void UpdateMedia(Sprite icon, VideoClip video)
        {
            if (_iconContainer == null) return;

            _iconContainer.Clear();
            _iconContainer.style.backgroundImage = default;

            if (video != null)
            {
                _iconContainer.style.display = DisplayStyle.Flex;
                _iconContainer.AddToClassList("media-container--video");

                if (_videoTexture == null || (int)_videoTexture.width != (int)video.width || (int)_videoTexture.height != (int)video.height)
                {
                    if (_videoTexture != null) _videoTexture.Release();
                    _videoTexture = new RenderTexture((int)video.width, (int)video.height, 0);
                }

                if (_videoPlayer.clip != video || !_videoPlayer.isPlaying)
                {
                    _videoPlayer.clip = video;
                    _videoPlayer.targetTexture = _videoTexture;
                    _videoPlayer.Play();
                }

                _iconContainer.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_videoTexture));
            }
            else if (icon != null)
            {
                _iconContainer.style.display = DisplayStyle.Flex;
                _iconContainer.RemoveFromClassList("media-container--video");
                _iconContainer.style.backgroundImage = new StyleBackground(icon);
                _videoPlayer.Stop();
            }
            else
            {
                _iconContainer.style.display = DisplayStyle.None;
                _iconContainer.RemoveFromClassList("media-container--video");
                _videoPlayer.Stop();
            }
        }

        private void PlayStepTransition()
        {
            if (_panel == null) return;

            // Brief scale pulse via USS transition
            _panel.AddToClassList("tutorial-panel--transitioning");
            _panel.schedule.Execute(() =>
            {
                _panel.RemoveFromClassList("tutorial-panel--transitioning");
            }).ExecuteLater((long)(_animationDuration * 1000));
        }

        private void Update()
        {
            if (!_isVisible || _currentAnchor == null) return;

            Vector3 target = CalculateSafePosition();

            // Only move if the delta is meaningful, so a still player sees a still card.
            if (Vector3.Distance(transform.position, target) > 0.005f)
            {
                transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * _positionLerpSpeed);
            }

            // Rotation is deliberately NOT handled here — TutorialBillboard owns it. Both used to
            // drive this transform every frame, and its LateUpdate snap always beat this slerp.
        }

        /// <summary>
        /// Places the card on the line from the player's eyes to the step's anchor, so it reads as
        /// belonging to the object being taught, then keeps it inside a comfortable reading band and
        /// in front of anything that would hide it. This is the arrangement VR tutorials generally
        /// settle on: anchored in the world, billboarded, never further than you can read and never
        /// closer than you can focus.
        ///
        /// Replaces an older solver that pushed the card straight up out of geometry in 0.2 m steps
        /// while sphere-casting every frame — that could park the card above the player's head, and
        /// it did nothing about the card simply being too far away to read.
        /// </summary>
        private Vector3 CalculateSafePosition()
        {
            if (_currentAnchor == null) return transform.position;

            Vector3 anchorPoint = _currentAnchor.position + _currentOffset;

            Camera cam = Camera.main;
            if (cam == null) return anchorPoint;

            Vector3 eye = cam.transform.position;
            Vector3 toAnchor = anchorPoint - eye;
            float anchorDistance = toAnchor.magnitude;

            // Anchor sitting on top of the player — nothing sensible to aim along.
            if (anchorDistance < 0.0001f) return anchorPoint;

            Vector3 direction = toAnchor / anchorDistance;
            float distance = Mathf.Clamp(anchorDistance, _minDistance, _maxDistance);

            // If something solid stands between the player and that point, bring the card in front
            // of it. Sliding toward the player keeps the card visible; pushing it up used to move it
            // somewhere the player was not looking.
            if (Physics.Raycast(eye, direction, out RaycastHit hit, distance, _occluderMask, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(transform))
            {
                distance = Mathf.Max(_minDistance, hit.distance - _occlusionPadding);
            }

            return eye + direction * distance;
        }

        private void OnDisable()
        {
            _eventService?.Unsubscribe<LanguageChangedEvent>(HandleLanguageChanged);

            if (_skipButton != null)
            {
                _skipButton.clicked -= OnSkipClicked;
            }
            if (_nextButton != null)
            {
                _nextButton.clicked -= OnNextClicked;
            }

            _videoPlayer?.Stop();
        }

        private void OnDestroy()
        {
            if (_videoTexture != null)
            {
                _videoTexture.Release();
                _videoTexture = null;
            }
        }
    }
}
