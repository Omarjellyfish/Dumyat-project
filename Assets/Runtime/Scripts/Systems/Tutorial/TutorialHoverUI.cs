using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
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
        [Header("Animation")]
        [SerializeField, Range(0.05f, 0.5f)] private float _animationDuration = 0.2f;

        private UIDocument _uiDocument;
        private VisualElement _root;
        private VisualElement _panel;
        private Label _instructionLabel;
        private Label _stepCounterLabel;
        private VisualElement _progressDotsContainer;
        private VisualElement _iconContainer;
        private Button _skipButton;

        private ITutorialService _tutorialService;
        private Transform _currentAnchor;
        private Vector3 _currentOffset;
        private Coroutine _animationCoroutine;
        private bool _isVisible;

        [Inject]
        private void Inject(ITutorialService tutorialService)
        {
            _tutorialService = tutorialService;
        }

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            if (_uiDocument == null || _uiDocument.rootVisualElement == null) return;

            _root = _uiDocument.rootVisualElement;
            CacheElements();
            SetupSkipButton();

            // Start hidden
            _panel?.AddToClassList("tutorial-panel--hidden");
        }

        private void CacheElements()
        {
            _panel = _root.Q<VisualElement>("TutorialPanel");
            _instructionLabel = _root.Q<Label>("InstructionText");
            _stepCounterLabel = _root.Q<Label>("StepCounter");
            _progressDotsContainer = _root.Q<VisualElement>("ProgressDots");
            _iconContainer = _root.Q<VisualElement>("IconContainer");
            _skipButton = _root.Q<Button>("SkipButton");
        }

        private void SetupSkipButton()
        {
            if (_skipButton != null)
            {
                _skipButton.clicked += OnSkipClicked;
            }
        }

        private void OnSkipClicked()
        {
            _tutorialService?.SkipTutorial();
        }

        private void LateUpdate()
        {
            // Follow the anchor in world space
            if (_isVisible && _currentAnchor != null)
            {
                transform.position = _currentAnchor.position + _currentOffset;
            }
        }

        /// <summary>
        /// Display a tutorial step on the hover panel.
        /// </summary>
        public void ShowStep(string instructionText, int stepIndex, int totalSteps,
            Sprite icon, Transform anchor, Vector3 offset)
        {
            // Update anchor tracking
            _currentAnchor = anchor;
            _currentOffset = offset;

            // Position immediately if we have an anchor
            if (anchor != null)
            {
                transform.position = anchor.position + offset;
            }

            // Update content
            if (_instructionLabel != null)
            {
                _instructionLabel.text = instructionText;
            }

            if (_stepCounterLabel != null)
            {
                _stepCounterLabel.text = $"Step {stepIndex + 1}/{totalSteps}";
            }

            // Update progress dots
            UpdateProgressDots(stepIndex, totalSteps);

            // Update icon
            UpdateIcon(icon);

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

        private void UpdateIcon(Sprite icon)
        {
            if (_iconContainer == null) return;

            _iconContainer.Clear();

            if (icon != null)
            {
                _iconContainer.style.display = DisplayStyle.Flex;
                _iconContainer.style.backgroundImage = new StyleBackground(icon);
            }
            else
            {
                _iconContainer.style.display = DisplayStyle.None;
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

        private void OnDisable()
        {
            if (_skipButton != null)
            {
                _skipButton.clicked -= OnSkipClicked;
            }
        }
    }
}
