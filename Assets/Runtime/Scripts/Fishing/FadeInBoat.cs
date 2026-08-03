using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace DumyatRuntime
{
    public class FadeInBoat : MonoBehaviour
    {
        [Header("Fade Settings")]
        [SerializeField] private float fadeInDuration = 1f;
        [SerializeField] private float fadeOutDuration = 1f;

        [Header("Fade Out Trigger")]
        [Tooltip("When the boat reaches this transform's position, fade out is triggered automatically.")]
        [SerializeField] private Transform fadeOutTarget;
        [SerializeField] private float fadeOutReachDistance = 0.5f;

        private CanvasGroup _fadeCanvasGroup;
        private bool _fadeOutTriggered;
        private bool _isFading;

        private void Update()
        {
            if (fadeOutTarget == null || _fadeOutTriggered)
                return;

            float distance = Vector3.Distance(transform.position, fadeOutTarget.position);

            if (distance <= fadeOutReachDistance)
            {
                _fadeOutTriggered = true;
                TriggerFadeOut();
            }
        }

        /// <summary>
        /// Call this to start the fade-in (screen goes from clear to black,
        /// or however you've configured the overlay).
        /// </summary>
        public async void TriggerFadeIn()
        {
            if (_isFading) return;

            EnsureFadeCanvas();
            _isFading = true;

            await FadeAsync(_fadeCanvasGroup.alpha, 1f, fadeInDuration);

            _isFading = false;
        }

        /// <summary>
        /// Call this to fade back out (black to clear). Also fired
        /// automatically when the boat reaches fadeOutTarget.
        /// </summary>
        public async void TriggerFadeOut()
        {
            if (_isFading) return;

            EnsureFadeCanvas();
            _isFading = true;

            await FadeAsync(_fadeCanvasGroup.alpha, 0f, fadeOutDuration);

            _isFading = false;
        }

        /// <summary>
        /// Allows the fade-out distance check to run again (e.g. if the
        /// boat is reset to a starting position and needs to re-trigger
        /// the sequence later).
        /// </summary>
        public void ResetFadeOutTrigger()
        {
            _fadeOutTriggered = false;
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