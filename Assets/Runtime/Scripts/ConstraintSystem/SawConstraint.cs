using UnityEngine;

namespace DumyatRuntime
{
    public class SawConstraint : SurfaceConstraint
    {
        public enum BladeDirectionAxis { Forward, Right, Up }

        [Header("Model Axis Alignment")]
        [SerializeField] private BladeDirectionAxis _bladeLengthAxis = BladeDirectionAxis.Forward;

        [Header("Blade Mesh Correction")]
        [Tooltip("Some saw blade meshes are modeled with a slight downward stair-step tilt " +
                 "along their own length instead of a perfectly straight edge. This rotates the " +
                 "VISUAL mesh around its own local Z axis (after the surface snap) to compensate, " +
                 "so the cutting edge reads as flush/flat against the wood. Tune in the inspector " +
                 "until the blade edge looks straight while constrained.")]
        [SerializeField] private float _zAxisCorrectionDegrees = 11f;

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

            // 4. Anchor position = surface hit point + clearance offset + stroke movement.
            //    This is where the HANDLE (this transform) would be while constrained - not
            //    where the mesh should render, since the mesh has its own authored offset.
            Vector3 anchorPosition = _hitPosition + _automaticSnapOffset + lockedMovement;

            // 5. Re-apply the mesh's authored local offset on top of the anchor + locked
            //    rotation, exactly the same way the unconstrained branch does it - otherwise
            //    the mesh would snap to sit exactly at the anchor with no offset at all.
            //    Then apply the extra Z correction, in the mesh's OWN local space (that's why
            //    it's multiplied on the right, after everything else), to cancel out the
            //    blade's built-in stair-step tilt so it reads flush against the surface.
            Quaternion zCorrection = Quaternion.Euler(0f, 0f, _zAxisCorrectionDegrees);

            _visualObject.transform.position = anchorPosition + (_lockedRotation * _visualLocalOffsetPosition);
            _visualObject.transform.rotation = _lockedRotation * _visualLocalOffsetRotation * zCorrection;
        }
    }
}