using UnityEngine;

namespace DumyatRuntime
{
    public class SawConstraint : SurfaceConstraint
    {
        public enum BladeDirectionAxis { Forward, Right, Up }

        [Header("Model Axis Alignment")]
        [SerializeField] private BladeDirectionAxis _bladeLengthAxis = BladeDirectionAxis.Forward;

        protected override void ConstrainMovement()
        {
            // 1. Get smooth real-time hand movement tracking delta
            Vector3 delta = GetMovementSinceSnap();

            // 2. Determine your custom model axis direction
            Vector3 sawingAxisVector = _lockedRotation * Vector3.forward;
            if (_bladeLengthAxis == BladeDirectionAxis.Right) sawingAxisVector = _lockedRotation * Vector3.right;
            if (_bladeLengthAxis == BladeDirectionAxis.Up)    sawingAxisVector = _lockedRotation * Vector3.up;

            // 3. Keep motion restricted strictly along that single cutting line
            float strokeAmount = Vector3.Dot(delta, sawingAxisVector);
            Vector3 lockedMovement = sawingAxisVector * strokeAmount;

            // 4. Position = Pure surface hit point + our automatically captured structural offset + physical movement
            _visualObject.transform.position = _hitPosition + _automaticSnapOffset + lockedMovement;

            // 5. Retain orientation
            _visualObject.transform.rotation = _lockedRotation;
        }
    }
}