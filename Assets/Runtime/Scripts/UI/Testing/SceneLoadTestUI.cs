using DumyatRuntime;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime.Testing
{
    [RequireComponent(typeof(UIDocument))]
    public class SceneLoadTestUI : MonoBehaviour
    {
        private ISceneLoadService _sceneLoadService;
        private IEventService _eventService;

        [Header("Scene Settings")]
        [Tooltip("The name of the scene to load into")]
        [SerializeField] private string _inspectorTargetScene = "TargetScene";

        [Tooltip("Optional: The name of the VR loading environment scene (e.g. 'Car')")]
        [SerializeField] private string _inspectorLoadingScene = "";

        private Button _loadButton;
        private VisualElement _loadingOverlay;
        private Label _progressLabel;
        private ProgressBar _progressBar;
        [Inject]
        private void Inject(ISceneLoadService sceneLoadService, IEventService eventService)
        {
            _sceneLoadService = sceneLoadService;
            _eventService = eventService;
        }

        private void OnEnable()
        {
            var uiDocument = GetComponent<UIDocument>();
            var root = uiDocument.rootVisualElement;

            // Bind UI elements
            _loadButton = root.Q<Button>("LoadButton");
            _loadingOverlay = root.Q<VisualElement>("LoadingOverlay");
            _progressLabel = root.Q<Label>("ProgressLabel");
            _progressBar = root.Q<ProgressBar>("ProgressBar");

            if (_loadButton != null)
                _loadButton.clicked += OnLoadButtonClicked;

            if (_eventService != null)
            {
                _eventService.Subscribe<SceneLoadBeginEvent>(OnLoadBegin);
                _eventService.Subscribe<SceneLoadProgressEvent>(OnLoadProgress);
                _eventService.Subscribe<SceneLoadCompleteEvent>(OnLoadComplete);
            }
        }

        private void OnDisable()
        {
            if (_loadButton != null)
                _loadButton.clicked -= OnLoadButtonClicked;

            if (_eventService != null)
            {
                _eventService.Unsubscribe<SceneLoadBeginEvent>(OnLoadBegin);
                _eventService.Unsubscribe<SceneLoadProgressEvent>(OnLoadProgress);
                _eventService.Unsubscribe<SceneLoadCompleteEvent>(OnLoadComplete);
            }
        }

        private void OnLoadButtonClicked()
        {
            if (string.IsNullOrEmpty(_inspectorTargetScene))
            {
                Debug.LogWarning("SceneLoadTestUI: Please enter a target scene name in the inspector.");
                return;
            }

            // Trigger the service using the inspector variables
            _sceneLoadService.LoadScene(_inspectorTargetScene, _inspectorLoadingScene);
        }

        private void OnLoadBegin(SceneLoadBeginEvent evt)
        {
            // Show the loading overlay
            if (_loadingOverlay != null)
                _loadingOverlay.style.display = DisplayStyle.Flex;

            if (_progressLabel != null)
                _progressLabel.text = "0%";

            if (_progressBar != null)
                _progressBar.value = 0f;
        }

        private void OnLoadProgress(SceneLoadProgressEvent evt)
        {
            // Update the progress bar and text
            if (_progressLabel != null)
                _progressLabel.text = Mathf.RoundToInt(evt.Progress * 100) + "%";

            if (_progressBar != null)
                _progressBar.value = evt.Progress;
        }

        private void OnLoadComplete(SceneLoadCompleteEvent evt)
        {
            // Hide the loading overlay when completely finished
            if (_loadingOverlay != null)
                _loadingOverlay.style.display = DisplayStyle.None;
        }
    }
}
