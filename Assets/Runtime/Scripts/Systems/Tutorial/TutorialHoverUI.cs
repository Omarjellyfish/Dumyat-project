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

        private VideoPlayer _videoPlayer;
        private RenderTexture _videoTexture;

        [Inject]
        private void Inject(ITutorialService tutorialService)
        {
            _tutorialService = tutorialService;
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
        public void ShowStep(TutorialStepSO step, int stepIndex, int totalSteps, Transform anchor, Vector3 offset)
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
                _instructionLabel.text = step.InstructionText;
            }

            if (_stepCounterLabel != null)
            {
                _stepCounterLabel.text = $"Step {stepIndex + 1}/{totalSteps}";
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

        private void OnDisable()
        {
            if (_skipButton != null)
            {
                _skipButton.clicked -= OnSkipClicked;
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
