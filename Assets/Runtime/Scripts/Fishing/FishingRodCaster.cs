using Reflex.Attributes;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    /// <summary>
    /// Turns a swing of the rod into a cast.
    ///
    /// <b>Why the Editor never caught the bug this used to have.</b> The trigger was read through
    /// <c>UnityEngine.XR.InputDevices</c> — the legacy XR input path, and the only call site of it in this
    /// project. That API is fed by the XR input subsystem; the XR Device Simulator is an Input System
    /// device provider and does not feed it at all. So in the Editor the button was permanently
    /// unpressed, no cast ever executed, and the tester lowered the hook with the reel instead. Every
    /// headset player took a code path that had never once run on a developer's machine.
    ///
    /// The cast is now driven by the XRI activate action on whichever hand is actually holding the rod,
    /// which the simulator does drive. The legacy button is still polled as a second source, because the
    /// tutorial tells the player to press A and that instruction should keep working.
    /// </summary>
    public class FishingRodCaster : MonoBehaviour
    {
        [Header("References")]
        public Transform rodTip;
        public Rigidbody hookRb;
        public FishingLineController fishingLine;

        [Header("Casting")]
        public float castMultiplier = 1.5f;
        public float minimumCastVelocity = 1f;
        public LayerMask waterLayer;
        public float settleVelocityThreshold = 0.2f;

        [Header("Cast Safety")]
        [Tooltip("A cast is force-settled after this long in the air. Without it, a hook that never " +
                 "reaches water leaves the rod unable to cast AND unable to reel — permanently.")]
        [SerializeField] private float _maxCastSeconds = 12f;

        [Tooltip("Radius of the water probe. Generous on purpose: it is sampled once per frame against " +
                 "a hook that can cover 20cm between frames, and a probe smaller than that gets skipped " +
                 "clean past.")]
        [SerializeField] private float _waterProbeRadius = 0.35f;

        [Tooltip("Below this speed the hook counts as come to rest, which ends the cast wherever it is.")]
        [SerializeField] private float _restSpeed = 0.35f;

        [Tooltip("How long the hook must stay below the rest speed before the cast is considered over.")]
        [SerializeField] private float _restSeconds = 0.6f;

        [SerializeField] private AudioClip castSound;
        [SerializeField] private SpringJoint springJoint;

        private bool _isHoldingCast;
        private bool _isCasting;

        private Vector3 _previousTipPosition;
        private Vector3 _velocityAverage;
        private Vector3 _bestVelocity;

        private float _castStartedTime;
        private float _restingSince;

        private bool _previousPrimaryButtonState;
        private XRGrabInteractable _interactable;

        private ISoundService _soundService;
        private IEventService _eventService;

        [Inject]
        private void Inject(ISoundService soundService, IEventService eventService)
        {
            _soundService = soundService;
            _eventService = eventService;
        }

        private void Awake()
        {
            _interactable = GetComponentInParent<XRGrabInteractable>();
        }

        private void OnEnable()
        {
            if (_interactable == null) return;

            _interactable.activated.AddListener(OnActivated);
            _interactable.deactivated.AddListener(OnDeactivated);
            _interactable.selectExited.AddListener(OnReleased);
        }

        private void OnDisable()
        {
            if (_interactable == null) return;

            _interactable.activated.RemoveListener(OnActivated);
            _interactable.deactivated.RemoveListener(OnDeactivated);
            _interactable.selectExited.RemoveListener(OnReleased);
        }

        private void Start()
        {
            if (rodTip == null)
            {
                Debug.LogWarning($"'{name}' has no rodTip assigned — this rod cannot cast.", this);
                enabled = false;
                return;
            }

            _previousTipPosition = rodTip.position;
        }

        private void Update()
        {
            TrackRodVelocity();
            PollLegacyButton();

            if (_isCasting) UpdateCastFlight();
        }

        /// <summary>True while the rod is in someone's hand. A cast from a rod lying on the floor is not
        /// a cast — the button used to be polled globally, so pressing A across the room fired one.</summary>
        private bool IsHeld => _interactable == null || _interactable.isSelected;

        private void TrackRodVelocity()
        {
            Vector3 velocity = (rodTip.position - _previousTipPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
            _previousTipPosition = rodTip.position;

            // Headset tracking is noisy enough that a single frame of jitter clears a 1 m/s threshold on
            // its own. Averaging over a few frames measures the swing rather than the noise.
            _velocityAverage = Vector3.Lerp(_velocityAverage, velocity, 0.5f);

            if (!_isHoldingCast) return;

            // Keep the whole vector, not a scalar projected onto one axis. The old version measured speed
            // along rodTip.forward and then threw along rodTip.right — ninety degrees apart — so a swing
            // that registered was launched somewhere the player had not aimed, and a swing along the
            // throw axis measured as nothing at all.
            if (_velocityAverage.magnitude > _bestVelocity.magnitude)
            {
                _bestVelocity = _velocityAverage;
            }
        }

        private void PollLegacyButton()
        {
            InputDevice rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            bool pressed = false;
            if (rightHand.isValid)
            {
                rightHand.TryGetFeatureValue(CommonUsages.primaryButton, out pressed);
            }

            if (pressed && !_previousPrimaryButtonState) BeginCast();
            if (!pressed && _previousPrimaryButtonState) ReleaseCast();

            _previousPrimaryButtonState = pressed;
        }

        private void OnActivated(ActivateEventArgs args) => BeginCast();

        private void OnDeactivated(DeactivateEventArgs args) => ReleaseCast();

        private void OnReleased(SelectExitEventArgs args)
        {
            // Dropping the rod mid-wind-up should abandon the wind-up, not leave it armed forever.
            _isHoldingCast = false;
            _bestVelocity = Vector3.zero;
        }

        private void BeginCast()
        {
            if (_isCasting) return;
            if (!IsHeld) return;

            _isHoldingCast = true;
            _bestVelocity = Vector3.zero;

            _eventService?.Publish(new RodCastArmedEvent());
        }

        private void ReleaseCast()
        {
            if (!_isHoldingCast) return;

            _isHoldingCast = false;

            float launchSpeed = _bestVelocity.magnitude;

            if (launchSpeed < minimumCastVelocity)
            {
                // Deliberately recoverable and deliberately quiet on the bus: a swing too limp to throw
                // anything is not a cast, and reporting it as one would teach the tutorial's player the
                // wrong motion. Nothing is latched here, so they can simply try again.
                _bestVelocity = Vector3.zero;
                return;
            }

            _isCasting = true;
            _castStartedTime = Time.time;
            _restingSince = 0f;

            fishingLine.EnterFreeCast();

            hookRb.linearVelocity = _bestVelocity.normalized * launchSpeed * castMultiplier;
            hookRb.angularVelocity = Vector3.zero;
            hookRb.WakeUp();

            _bestVelocity = Vector3.zero;

            _soundService?.PlaySoundFxAtPosition(castSound, transform, 1f);
            _eventService?.Publish(new RodCastReleasedEvent(launchSpeed));
        }

        /// <summary>
        /// Watches a cast in flight and ends it — on water, at rest, or on the clock.
        ///
        /// Only the first of those used to exist, and it tested a layer that in Ras El Bar belongs to a
        /// different object than the one the hook's own water trigger tests. Missing it left three latches
        /// set with no way to clear any of them: no further cast, no reel, and a neutered spring. One
        /// throw onto the deck ended fishing for the session, silently.
        /// </summary>
        private void UpdateCastFlight()
        {
            if (FishingWater.IsPointInWater(hookRb.position, _waterProbeRadius, WaterMask))
            {
                SettleCast("landed in water", true);
                return;
            }

            if (hookRb.linearVelocity.sqrMagnitude <= _restSpeed * _restSpeed)
            {
                if (_restingSince == 0f) _restingSince = Time.time;

                if (Time.time - _restingSince >= _restSeconds)
                {
                    SettleCast("came to rest out of the water", false);
                }
            }
            else
            {
                _restingSince = 0f;
            }

            if (Time.time - _castStartedTime >= _maxCastSeconds)
            {
                SettleCast("timed out in flight", false);
            }
        }

        private void SettleCast(string reason, bool inWater)
        {
            _isCasting = false;

            fishingLine.SettleCastAtCurrentDistance();

            if (inWater)
            {
                fishingLine.LockHookPosition();

                _eventService?.Publish(new HookLandedInWaterEvent(
                    hookRb.position,
                    Vector3.Distance(rodTip.position, hookRb.position)));
            }
            else
            {
                // Not water, so nothing is fishing — but the rod must still be usable. Everything is
                // released here so the player can reel in and cast again.
                Debug.Log($"Cast {reason} — reel in and try again.", this);
            }
        }

        // Anything tagged Water counts too, so a scene that only tagged its water still works.
        private LayerMask WaterMask => waterLayer.value != 0 ? waterLayer : (LayerMask)(1 << 4);
    }
}
