using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace DumyatRuntime
{
    /// <summary>
    /// Plain C# service for fade-to-black scene transitions.
    /// Registered via RegisterType in GameInstaller (same pattern as SceneLoadService).
    /// Creates its own Canvas/CanvasGroup on first use.
    /// </summary>
    public class FadeTransitionHandler : IFadeTransitionService
    {
        private const float DEFAULT_FADE_DURATION = 1f;

        private readonly IEventService _eventService;
        private readonly ISceneLoadService _sceneLoadService;

        private CanvasGroup _fadeCanvasGroup;
        private bool _isTransitioning;
        private GameObject _playerObj;

        public bool IsTransitioning => _isTransitioning;

        public FadeTransitionHandler(IEventService eventService, ISceneLoadService sceneLoadService)
        {
            _eventService = eventService;
            _sceneLoadService = sceneLoadService;
            _eventService.Subscribe<SceneLoadCompleteEvent>(OnSceneLoadComplete);
        }

        public async void StartTransition(string destinationScene)
        {
            if (_isTransitioning) return;

            _isTransitioning = true;
            EnsureFadeCanvas();

            // Find and persist the player
            _playerObj = GameObject.FindWithTag("Player");
            if (_playerObj != null)
            {
                if (_playerObj.transform.parent != null)
                {
                    _playerObj.transform.SetParent(null);
                }
                Object.DontDestroyOnLoad(_playerObj);
            }
            else
            {
                Debug.LogWarning("FadeTransitionHandler: Could not find GameObject with tag 'Player'.");
            }

            // Fade out
            await FadeAsync(0f, 1f, DEFAULT_FADE_DURATION);

            // Load the scene (SceneLoadService publishes SceneLoadCompleteEvent when done)
            _sceneLoadService.LoadScene(destinationScene);
        }

        private async void OnSceneLoadComplete(SceneLoadCompleteEvent evt)
        {
            if (!_isTransitioning) return;

            // Teleport the player
            if (_playerObj != null)
            {
                GameObject arrivalPoint = GameObject.FindWithTag("ArrivalPoint");
                if (arrivalPoint != null)
                {
                    _playerObj.transform.position = arrivalPoint.transform.position;
                    _playerObj.transform.rotation = arrivalPoint.transform.rotation;
                }
                else
                {
                    Debug.LogWarning("FadeTransitionHandler: Could not find GameObject with tag 'ArrivalPoint' in the new scene.");
                }

                // Move player into the newly loaded active scene so they don't stay in DontDestroyOnLoad
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_playerObj, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                _playerObj = null;
            }

            EnsureFadeCanvas();
            await FadeAsync(1f, 0f, DEFAULT_FADE_DURATION);
            _isTransitioning = false;
        }

        private async Task FadeAsync(float from, float to, float duration)
        {
            float timer = 0f;
            while (timer < duration)
            {
                timer += Time.unscaledDeltaTime;
                _fadeCanvasGroup.alpha = Mathf.Lerp(from, to, timer / duration);
                await Task.Yield();
            }
            _fadeCanvasGroup.alpha = to;
        }

        private void EnsureFadeCanvas()
        {
            if (_fadeCanvasGroup != null) return;

            var canvasObj = new GameObject("FadeCanvas");
            Object.DontDestroyOnLoad(canvasObj);

            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;

            canvasObj.AddComponent<CanvasScaler>();

            var imgObj = new GameObject("FadeImage");
            imgObj.transform.SetParent(canvasObj.transform, false);

            var img = imgObj.AddComponent<Image>();
            img.color = Color.black;

            var rect = img.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _fadeCanvasGroup = canvasObj.AddComponent<CanvasGroup>();
            _fadeCanvasGroup.alpha = 0f;
            _fadeCanvasGroup.blocksRaycasts = false;
            _fadeCanvasGroup.interactable = false;
        }
    }
}
