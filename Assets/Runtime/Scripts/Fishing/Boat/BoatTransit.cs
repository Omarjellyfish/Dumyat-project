using System.Collections;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace DumyatRuntime
{
    /// <summary>
    /// Carries the player between the jetty and the fishing boat, and owns everything that has to
    /// happen together when they do: the blackout, moving the rig and the father and the rod, locking
    /// walking while aboard, and the father's closing remark on the way back.
    ///
    /// One component owns all of it because these have to agree. They were previously spread over a
    /// select-handler on the father, a raw CanvasGroup that was never assigned in the scene (so the
    /// fade silently did nothing at all), and a movement lock keyed off a toggle bool — and any one of
    /// them getting out of step left the player walking about on a boat, or blacked out on the shore.
    ///
    /// The player never talks to this directly. Two <see cref="BoatBoardUI"/> panels do — one on the
    /// jetty, one on the gunwale.
    /// </summary>
    public class BoatTransit : MonoBehaviour
    {
        [System.Serializable]
        private struct Destination
        {
            [Tooltip("Where the player lands. Only its heading is used — see PlayerRigTeleport.")]
            public Transform playerPoint;

            [Tooltip("Where the father stands after this move — beside the player, not on top of them.")]
            public Transform fatherPoint;

            [Tooltip("Where the fishing rod goes after this move. Ignored while the rod is held.")]
            public Transform rodPoint;
        }

        [Header("Destinations")]
        [SerializeField] private Destination boat;
        [SerializeField] private Destination shore;

        [Header("Cast")]
        [Tooltip("The father, moved along so he is always beside the player. Optional.")]
        [SerializeField] private Transform father;

        [Tooltip("The fishing rod, moved along so it is always within reach. Optional.")]
        [SerializeField] private Transform fishRod;

        [Tooltip("The rod's grab interactable. While the rod is held it is left alone — it is parented " +
                 "to the controller and already travels with the rig, and repositioning it would tear " +
                 "it out of the player's hand.")]
        [SerializeField] private XRGrabInteractable fishRodInteractable;

        [Header("Movement Lock")]
        [Tooltip("LEGACY — no longer read. Locking is done by PlayerLocomotionLock, which switches off " +
                 "every locomotion provider on the rig except turning. Kept only so existing scenes do " +
                 "not report a missing field; safe to clear.")]
        [SerializeField] private GameObject moveProvider;

        [Tooltip("LEGACY — no longer read. See moveProvider.")]
        [SerializeField] private string moveProviderTag = "Move";

        [Header("Fade")]
        [SerializeField] private float fadeOutSeconds = 0.35f;
        [SerializeField] private float fadeInSeconds = 0.4f;

        [Header("Narrative")]
        [Tooltip("The father's line for coming back ashore. Plays once, on the first return.")]
        [SerializeField] private OneShotDialogueCue returnCue;

        private IScreenFadeService _fade;
        private IEventService _eventService;

        private bool _isMoving;
        private bool _lockApplied;
        private FatherPathMover _fatherMover;

        /// <summary>True while the player is on the boat.</summary>
        public bool IsAboard { get; private set; }

        [Inject]
        private void Inject(IScreenFadeService fade, IEventService eventService)
        {
            _fade = fade;
            _eventService = eventService;
        }

        // No Start-time resolution of the rig or the move provider. RasElBar authors NO XR rig — it is
        // spawned by PlayerRigSpawner on SceneLoadCompleteEvent — so anything looking for the player by
        // tag during Start finds nothing and caches null forever. Everything here resolves on demand.

        private void OnDisable()
        {
            // Not optional. A scene change or a disable while the player is aboard would otherwise ship
            // them into the next scene unable to walk, with nothing on screen to explain why and no way
            // back — the lock lives on the rig, which outlives this component.
            if (_lockApplied) SetWalkingLocked(false);
        }

        /// <summary>Takes the player out to the boat. Does nothing if they are already there.</summary>
        public void Board()
        {
            if (IsAboard) return;
            BeginMove(boat, aboard: true);
        }

        /// <summary>Brings the player back to the jetty. Does nothing if they are already ashore.</summary>
        public void LeaveToShore()
        {
            if (!IsAboard) return;
            BeginMove(shore, aboard: false);
        }

        private void BeginMove(Destination destination, bool aboard)
        {
            if (_isMoving) return;

            if (destination.playerPoint == null)
            {
                Debug.LogWarning($"{name}: the {(aboard ? "boat" : "shore")} destination has no player " +
                                 "point assigned — cannot move.", this);
                return;
            }

            Transform rig = PlayerRigTeleport.FindRig();
            if (rig == null)
            {
                Debug.LogWarning($"{name}: no player rig found — is it spawned yet?", this);
                return;
            }

            StartCoroutine(MoveRoutine(rig, destination, aboard));
        }

        private IEnumerator MoveRoutine(Transform rig, Destination destination, bool aboard)
        {
            _isMoving = true;

            // Unscaled, so a move that starts as the game pauses still finishes rather than stranding
            // the player on a black screen.
            _fade?.FadeToBlack(fadeOutSeconds);
            if (_fade != null) yield return new WaitForSecondsRealtime(fadeOutSeconds);

            PlayerRigTeleport.MoveTo(rig, destination.playerPoint);

            if (father != null && destination.fatherPoint != null)
            {
                // Stop him walking BEFORE he is moved. His scripted path is a coroutine stepping toward
                // waypoints on the shore; teleporting him out to the boat without stopping it leaves it
                // running, and he sets off from the deck back toward the jetty — straight across the
                // water. Once the player has taken him out to the boat the path has done its job.
                StopFatherWalking();

                father.SetPositionAndRotation(destination.fatherPoint.position,
                                              destination.fatherPoint.rotation);
            }

            if (fishRod != null && destination.rodPoint != null)
            {
                // The rod is placed EVERY time, from whatever is holding it — a socket or the player's
                // own hand. Arriving on the boat is meant to be the same arrangement every trip: player
                // here, father there, rod within reach. Letting a held rod travel with the hand instead
                // made the deck different depending on what the player happened to be carrying, and a
                // socket left holding it pinned it back to the shore stand entirely.
                ReleaseFromInteractors();
                fishRod.SetPositionAndRotation(destination.rodPoint.position,
                                               destination.rodPoint.rotation);

                // Land it still, or it keeps whatever swing the hand gave it and slides off the deck.
                if (fishRod.TryGetComponent(out Rigidbody rodBody) && !rodBody.isKinematic)
                {
                    rodBody.linearVelocity = Vector3.zero;
                    rodBody.angularVelocity = Vector3.zero;
                }
            }

            IsAboard = aboard;
            SetWalkingLocked(aboard);

            // Written out rather than a ternary: EventBus resolves a channel per concrete type, so a
            // ternary typed as IGameEvent would publish on the IGameEvent channel and reach nobody.
            if (aboard) _eventService?.Publish(new BoatBoardedEvent());
            else _eventService?.Publish(new BoatLeftEvent());

            // Spoken after the move, not before: the father's remark about being back ashore is only
            // true once the player is actually standing there.
            if (!aboard) returnCue?.TryPlay();

            _fade?.FadeFromBlack(fadeInSeconds);

            _isMoving = false;
        }

        /// <summary>
        /// Frees the rod from whatever is holding it — the shore stand's socket or the player's hand —
        /// so it can actually be put where the boat expects it.
        ///
        /// A socket pins the rod: move the transform and it snaps straight back the same frame. A hand
        /// parents it, so it would otherwise ride along and land wherever the player's arm happened to
        /// be. Releasing both is what makes the deck the same on every trip.
        /// </summary>
        private void ReleaseFromInteractors()
        {
            if (fishRodInteractable == null || !fishRodInteractable.isSelected) return;

            var manager = fishRodInteractable.interactionManager;
            if (manager == null) return;

            var holders = fishRodInteractable.interactorsSelecting;
            for (int i = holders.Count - 1; i >= 0; i--)
            {
                // Cast to the interfaces: the XRBaseInteractor/XRBaseInteractable overload is deprecated
                // and would win overload resolution otherwise.
                manager.SelectExit((IXRSelectInteractor)holders[i], (IXRSelectInteractable)fishRodInteractable);
            }
        }

        /// <summary>
        /// Ends the father's scripted walk for good. Resolved from the father transform rather than
        /// serialized, so a scene that wires the father but forgets the mover still behaves.
        /// </summary>
        private void StopFatherWalking()
        {
            if (_fatherMover == null && father != null)
                _fatherMover = father.GetComponent<FatherPathMover>();

            if (_fatherMover != null) _fatherMover.StopPath();
        }

        private void SetWalkingLocked(bool locked)
        {
            // Delegated to PlayerLocomotionLock, which switches off every LocomotionProvider on the rig
            // except turning.
            //
            // This used to disable a single GameObject found by the 'Move' tag, and it did not work.
            // RasElBar contains no object with that tag at all — the rig is spawned at runtime — and the
            // rig prefab it spawns carries a Teleportation provider, two teleport interactors and a Grab
            // Move provider besides the tagged walking object. So the lock either found nothing and gave
            // up, or switched off walking while leaving the player free to teleport off the boat.
            PlayerLocomotionLock.SetLocked(locked);
            _lockApplied = locked;
        }
    }
}
