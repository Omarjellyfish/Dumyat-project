using UnityEngine;
using UnityEngine.XR;
using Reflex;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public class FishingRodCaster : MonoBehaviour
    {
        [Header("References")]
        public Transform rodTip;
        public Rigidbody hookRb;
        public FishingLineController fishingLine;

        [Header("Casting")]
        public float castMultiplier = 1.5f;
        public float minimumCastVelocity = 1f;
        public LayerMask waterLayer;          // set this to your water surface layer
        public float settleVelocityThreshold = 0.2f;
        public float maxFlightTime = 5f;

        private float castTimer;

        private bool isHoldingCast;
        private bool isCasting;

        private Vector3 previousTipPosition;

        private float bestSpeed;

        private bool previousPrimaryButtonState;
        [SerializeField] private AudioClip castSound;
        [SerializeField] private SpringJoint springJoint;
        private ISoundService _soundService;

        [Inject]
        private void Inject(ISoundService soundService)
        {
            _soundService = soundService;
        }

        private void Start()
        {
            previousTipPosition = rodTip.position;
        }

        private void Update()
        {
            TrackRodVelocity();
            HandleCasting();
        }

        private void TrackRodVelocity()
        {
            Vector3 rodVelocity =
                (rodTip.position - previousTipPosition) /
                Mathf.Max(Time.deltaTime, 0.0001f);

            if (isHoldingCast)
            {
                float forwardSpeed =
                    Vector3.Dot(rodVelocity, rodTip.forward);

                if (forwardSpeed > bestSpeed)
                {
                    bestSpeed = forwardSpeed;
                }
            }

            previousTipPosition = rodTip.position;
        }

        private void HandleCasting()
        {
            InputDevice rightHand =
                InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            bool primaryButtonPressed = false;

            if (rightHand.isValid)
            {
                rightHand.TryGetFeatureValue(
                    CommonUsages.primaryButton,
                    out primaryButtonPressed);
            }

            if (primaryButtonPressed && !previousPrimaryButtonState)
            {
                BeginCast();
            }

            if (!primaryButtonPressed && previousPrimaryButtonState)
            {
                ReleaseCast();
            }

            previousPrimaryButtonState = primaryButtonPressed;

            if (isCasting)
            {
                UpdateLineLength();
            }
        }

        private void BeginCast()
        {
            if (isCasting)
                return;
            isHoldingCast = true;
            bestSpeed = 0f;
        }

        private void ReleaseCast()
        {
            isHoldingCast = false;

            if (bestSpeed < minimumCastVelocity)
                return;

            isCasting = true;
            castTimer = 0f;

            fishingLine.EnterFreeCast();

            // Set velocity directly for a punchy, predictable throw —
            // AddForce(VelocityChange) works too, but this reads more clearly
            // and isn't affected by residual velocity.
            hookRb.linearVelocity = rodTip.right * bestSpeed * castMultiplier;
            hookRb.angularVelocity = Vector3.zero;

            bestSpeed = 0f;

            _soundService.PlaySoundFxAtPosition(castSound, transform, 1f);
        }


        private void UpdateLineLength()
        {
            castTimer += Time.deltaTime;

            bool hitWater = Physics.CheckSphere(hookRb.position, 0.05f, waterLayer);

            if (hitWater || castTimer > maxFlightTime)
            {
                isCasting = false;
                fishingLine.SettleCastAtCurrentDistance();

                fishingLine.LockHookPosition();

            }
        }
    }
}