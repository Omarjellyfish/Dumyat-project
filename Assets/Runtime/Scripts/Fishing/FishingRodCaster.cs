using UnityEngine;

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

        private bool isHoldingCast;
        private bool isCasting;

        private Vector3 previousTipPosition;

        private float bestSpeed;
        private Vector3 bestDirection;

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
                    bestDirection = rodTip.forward;
                }
            }

            previousTipPosition = rodTip.position;
        }

        private void HandleCasting()
        {
            // Replace this with XR input later
            if (Input.GetKeyDown(KeyCode.Mouse0))
            {
                BeginCast();
            }

            if (Input.GetKeyUp(KeyCode.Mouse0))
            {
                ReleaseCast();
            }

            if (isCasting)
            {
                UpdateLineLength();
            }
        }

        private void BeginCast()
        {
            isHoldingCast = true;

            bestSpeed = 0f;
            bestDirection = rodTip.forward;
        }

        private void ReleaseCast()
        {
            isHoldingCast = false;

            if (bestSpeed < minimumCastVelocity)
                return;

            isCasting = true;

            hookRb.AddForce(
                bestDirection * bestSpeed * castMultiplier,
                ForceMode.VelocityChange);
        }

        private void UpdateLineLength()
        {
            float currentDistance =
                Vector3.Distance(
                    rodTip.position,
                    hookRb.position);

            fishingLine.SetCastingLength(currentDistance);

            if (currentDistance >= fishingLine.GetMaxLength() - 0.1f)
            {
                isCasting = false;
            }
        }
    }
}