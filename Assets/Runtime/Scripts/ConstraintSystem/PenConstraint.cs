using UnityEngine;

namespace DumyatRuntime
{
    public class PenConstraint : SurfaceConstraint
    {
        protected override void ConstrainMovement()
        {
            Vector3 delta = GetMovementSinceSnap();

            // Project movement straight onto the 2D surface plane
            float perpendicularAmount = Vector3.Dot(delta, _hitNormal);
            Vector3 slidingMovement = delta - (_hitNormal * perpendicularAmount);

            // Position = Pure surface hit point + automatically captured length of the pen + sliding travel path
            _visualObject.transform.position = _hitPosition + _automaticSnapOffset + slidingMovement;
            _visualObject.transform.rotation = Quaternion.LookRotation(transform.forward, _hitNormal);
        }
    }
}