using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit;

namespace DumyatRuntime
{
    /// <summary>
    /// Lets the player interact with the father NPC (via a plain XRSimpleInteractable, not a
    /// grab) to toggle-teleport between the boat and a designated outside point, with a
    /// fade-to-black transition and movement locked only while on the boat.
    ///
    /// Disabled by default. FatherPathMover enables this the moment its scripted path reaches
    /// the boat for the first time -- before that, the father is not interactable at all.
    ///
    /// Replaces FadeInBoat: this script now owns the fade, the teleport, and the movement
    /// lock together so they can't drift out of sync with each other.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class FatherTeleportInteractable : MonoBehaviour
    {
        [System.Serializable]
        private struct Destination
        {
            [Tooltip("Where the player lands.")]
            public Transform playerPoint;

            [Tooltip("Where the father stands after this teleport -- offset from the player, not on top of them.")]
            public Transform fatherPoint;

            [Tooltip("Where the fishing rod spawns after this teleport -- offset from the player, not on top of them.")]
            public Transform fishRodPoint;
        }

        [Header("Teleport Pair")]
        [SerializeField] private Destination boat;
        [SerializeField] private Destination outside;

        [Header("Player")]
        [Tooltip("Assign directly if the player exists in-scene at edit time. If left null, found by tag at runtime.")]
        [SerializeField] private Transform playerRoot;
        [SerializeField] private string playerTag = "Player";

        [Header("Fishing Rod")]
        [Tooltip("The fishing rod object to move along with the teleport. Leave null if not in play yet.")]
        [SerializeField] private Transform fishRod;
        [Tooltip("Optional -- if the rod is currently held, it won't be repositioned while selected.")]
        [SerializeField] private XRGrabInteractable fishRodInteractable;

        [Header("Movement Lock")]
        [Tooltip("GameObject tagged 'Move' (your movement controller) gets disabled only while at the boat.")]
        [SerializeField] private string moveControllerTag = "Move";

        [Header("Fade")]
        [Tooltip("Assign the CanvasGroup used for the fade-to-black image. Must be assigned -- this script does not build one at runtime.")]
        [SerializeField] private CanvasGroup fadeCanvasGroup;
        [SerializeField] private float fadeInDuration = 1f;
        [SerializeField] private float fadeOutDuration = 1f;

        private XRSimpleInteractable _interactable;
        private GameObject _movementController;
        private bool _isFading;

        // true = currently at the boat pair, false = currently at the outside pair.
        private bool _atBoat;

        private void Awake()
        {
            _interactable = GetComponent<XRSimpleInteractable>();

            if (fadeCanvasGroup == null)
                Debug.LogWarning($"{name}: Fade Canvas Group is not assigned in the Inspector -- fades will be skipped.", this);
        }

        private void OnEnable()
        {
            _interactable.selectEntered.AddListener(OnSelected);

            // The rig's fade canvas ships with Blocks Raycasts ON and alpha 0 — an invisible,
            // raycast-target Image 16 cm in front of the eyes, which every UI ray hits before it
            // reaches anything else. Alpha alone does not stop that: a CanvasGroup keeps blocking
            // while fully transparent. Whoever drives the fade has to drive this with it.
            SetFadeBlocking(fadeCanvasGroup != null && fadeCanvasGroup.alpha > 0f);

            EnsurePlayerRoot();
            _atBoat = IsPlayerCloserTo(boat.playerPoint);

            // Sync the lock to wherever we actually start.
            SetMovementLocked(_atBoat);
        }

        private void OnDisable()
        {
            _interactable.selectEntered.RemoveListener(OnSelected);
        }

        private async void OnSelected(SelectEnterEventArgs args)
        {
            if (_isFading) return;

            EnsurePlayerRoot();

            Destination destination = _atBoat ? outside : boat;

            if (playerRoot == null || destination.playerPoint == null)
            {
                Debug.LogWarning($"{name}: missing playerRoot or destination playerPoint, cannot teleport.");
                return;
            }

            _isFading = true;

            if (fadeCanvasGroup != null)
                await FadeAsync(fadeCanvasGroup.alpha, 1f, fadeInDuration);

            // Teleport everything while the screen is black.
            playerRoot.SetPositionAndRotation(destination.playerPoint.position, destination.playerPoint.rotation);

            if (destination.fatherPoint != null)
                transform.SetPositionAndRotation(destination.fatherPoint.position, destination.fatherPoint.rotation);

            if (fishRod != null && destination.fishRodPoint != null
                && (fishRodInteractable == null || !fishRodInteractable.isSelected))
            {
                fishRod.transform.SetPositionAndRotation(destination.fishRodPoint.position, destination.fishRodPoint.rotation);
            }

            _atBoat = !_atBoat;
            SetMovementLocked(_atBoat); // locked only when we just arrived at the boat

            if (fadeCanvasGroup != null)
                await FadeAsync(fadeCanvasGroup.alpha, 0f, fadeOutDuration);

            _isFading = false;
        }

        private void SetMovementLocked(bool locked)
        {
            if (_movementController == null)
                _movementController = GameObject.FindGameObjectWithTag(moveControllerTag);

            if (_movementController == null)
            {
                Debug.LogWarning($"{name}: no GameObject tagged '{moveControllerTag}' was found.", this);
                return;
            }

            // locked == true means movement is disabled, so the controller GameObject is INACTIVE.
            _movementController.SetActive(!locked);
        }

        private async Task FadeAsync(float from, float to, float duration)
        {
            // Blocking goes ON for the whole fade-to-black and stays on until the fade-out has
            // fully cleared — so nothing is clickable through a black screen mid-teleport, and the
            // overlay stops intercepting rays the instant it is transparent again.
            SetFadeBlocking(true);

            float timer = 0f;
            while (timer < duration)
            {
                timer += Time.unscaledDeltaTime;
                fadeCanvasGroup.alpha = Mathf.Lerp(from, to, timer / duration);
                await Task.Yield();
            }
            fadeCanvasGroup.alpha = to;

            SetFadeBlocking(to > 0f);
        }

        private void SetFadeBlocking(bool blocking)
        {
            if (fadeCanvasGroup == null) return;

            fadeCanvasGroup.blocksRaycasts = blocking;
            fadeCanvasGroup.interactable = blocking;
        }

        private void EnsurePlayerRoot()
        {
            if (playerRoot != null) return;

            var playerObj = GameObject.FindGameObjectWithTag(playerTag);
            if (playerObj != null)
                playerRoot = playerObj.transform;
            else
                Debug.LogWarning($"{name}: could not find a player tagged '{playerTag}' — is it spawned yet?");
        }

        private bool IsPlayerCloserTo(Transform point)
        {
            if (playerRoot == null || point == null) return false;

            float distToBoat = boat.playerPoint != null ? Vector3.Distance(playerRoot.position, boat.playerPoint.position) : float.MaxValue;
            float distToOutside = outside.playerPoint != null ? Vector3.Distance(playerRoot.position, outside.playerPoint.position) : float.MaxValue;

            return distToBoat <= distToOutside;
        }
    }
}