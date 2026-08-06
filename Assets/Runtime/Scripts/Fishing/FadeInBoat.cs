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
        [SerializeField] private Transform fatherFadeOutTarget;
        [SerializeField] private float fadeOutReachDistance = 0.5f;

        [SerializeField] private Transform fadeOutTargetLand;
        [SerializeField] private Transform fatherFadeOutTargetLand;


        [SerializeField] private Transform father;
        [SerializeField] private GameObject fishingRod;
        [SerializeField] private Transform fishingRodTransform;
        [SerializeField] private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable fishingRodInteractable;


        private CanvasGroup _fadeCanvasGroup;
        private bool _fadeOutTriggered;
        private bool _isFading;
        private GameObject _movementController;

        private void ToggleMovement()
        {
            // FindGameObjectWithTag can't find inactive objects, so cache the
            // reference once while it's still active instead of re-finding it
            // every call (which returned null after the first toggle disabled it).
            if (_movementController == null)
            {
                _movementController = GameObject.FindGameObjectWithTag("Move");
            }

            if (_movementController == null)
            {
                Debug.LogWarning("[FadeInBoat] No GameObject tagged 'Move' was found in the scene.", this);
                return;
            }

            _movementController.SetActive(!_movementController.activeSelf);
        }

        /// <summary>
        /// Finds the player by the "Player" tag. Called fresh each time instead of
        /// caching, so it stays correct even if the player object is swapped/respawned.
        /// </summary>
        private Transform FindPlayer()
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject == null)
            {
                Debug.LogWarning("[FadeInBoat] No GameObject tagged 'Player' was found in the scene.", this);
                return null;
            }

            return playerObject.transform;
        }

        /// <summary>
        /// Parameterless so it can be wired directly into a UI Button's OnClick().
        /// Finds the player by tag instead of relying on a passed-in Transform.
        /// </summary>
        public async void StartTransition()
        {
            Transform player = FindPlayer();
            if (player == null)
                return;

            if (_isFading)
                return;

            EnsureFadeCanvas();
            _isFading = true;

            // Fade to black
            await FadeAsync(_fadeCanvasGroup.alpha, 1f, fadeInDuration);

            // Teleport player while the screen is black
            if (fadeOutTarget != null)
            {
                player.position = fadeOutTarget.position;
                player.rotation = fadeOutTarget.rotation;

                father.position = fatherFadeOutTarget.position;
                father.rotation = fatherFadeOutTarget.rotation;

                ToggleMovement();

                if (!fishingRodInteractable.isSelected)
                {
                    fishingRod.transform.position = fishingRodTransform.position;
                    fishingRod.transform.rotation = fishingRodTransform.rotation;
                }
            }

            // Fade back to gameplay
            await FadeAsync(_fadeCanvasGroup.alpha, 0f, fadeOutDuration);

            _isFading = false;
        }

        /// <summary>
        /// Parameterless so it can be wired directly into a UI Button's OnClick().
        /// Finds the player by tag instead of relying on a passed-in Transform.
        /// </summary>
        public async void StartTransitionToLand()
        {
            Transform player = FindPlayer();
            if (player == null)
                return;

            if (_isFading)
                return;

            EnsureFadeCanvas();
            _isFading = true;

            ToggleMovement();

            // Fade to black
            await FadeAsync(_fadeCanvasGroup.alpha, 1f, fadeInDuration);

            // Teleport player while the screen is black
            player.position = fadeOutTargetLand.position;
            player.rotation = fadeOutTargetLand.rotation;

            father.position = fatherFadeOutTargetLand.position;
            father.rotation = fatherFadeOutTargetLand.rotation;

            // Fade back to gameplay
            await FadeAsync(_fadeCanvasGroup.alpha, 0f, fadeOutDuration);

            _isFading = false;
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