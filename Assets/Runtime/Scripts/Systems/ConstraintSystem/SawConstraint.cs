using UnityEngine;

namespace DumyatRuntime
{
public class SawConstraint : SurfaceConstraint
{
    public enum BladeDirectionAxis { Forward, Right, Up }

    [Header("Model Axis Alignment")]
    [SerializeField] private BladeDirectionAxis _bladeLengthAxis = BladeDirectionAxis.Forward;

    [Header("Cut Depth")]
    [Tooltip("How far (meters) the blade sinks into the wood per completed stroke.")]
    [SerializeField] private float _depthPerStroke = 0.002f;
    [Tooltip("Max total depth the blade is allowed to sink to.")]
    [SerializeField] private float _maxDepth = 0.02f;

    private float _currentDepth = 0f;

    public void AdvanceCutDepth()
    {
        _currentDepth = Mathf.Min(_currentDepth + _depthPerStroke, _maxDepth);
    }

    public void ResetCutDepth()
    {
        _currentDepth = 0f;
    }

    public override void SetCutProgress(int current, int required)
    {
        if (current <= 0 || required <= 0)
        {
            ResetCutDepth();
        }
        else if (current <= required)
        {
            AdvanceCutDepth();
        }

        base.SetCutProgress(current, required);
    }

    protected override void ConstrainMovement()
    {
        if (_visualObject == null)
            return;

        Vector3 delta = GetMovementSinceSnap();

        Vector3 sawingAxisVector = _lockedRotation * Vector3.forward;
        if (_bladeLengthAxis == BladeDirectionAxis.Right) sawingAxisVector = _lockedRotation * Vector3.right;
        if (_bladeLengthAxis == BladeDirectionAxis.Up)    sawingAxisVector = _lockedRotation * Vector3.up;

        float strokeAmount = Vector3.Dot(delta, sawingAxisVector);
        Vector3 lockedMovement = sawingAxisVector * strokeAmount;

        // Sink into the wood along the (inverse) surface normal as strokes accumulate.
        Vector3 depthOffset = -_hitNormal * _currentDepth;

        Vector3 anchorPosition = _hitPosition + _automaticSnapOffset + lockedMovement + depthOffset;

        _visualObject.transform.position = anchorPosition + (_lockedRotation * _visualLocalOffsetPosition);
        _visualObject.transform.rotation = _lockedRotation * _visualLocalOffsetRotation;
    }
}
}
