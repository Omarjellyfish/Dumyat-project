using UnityEngine;

public class FishingReelRope : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform ropeEnd;
    [SerializeField] private Transform reel;

    [Tooltip("Fixed position of the hook when rope length = 0")]
    [SerializeField] private Transform originalHookPosition;

    [Header("Rope Settings")]
    [SerializeField] private float minRopeLength = 0f;

    [SerializeField] private float maxRopeLength = 1000f;

    [Tooltip("How much rope length changes per full reel revolution (360°)")]
    [SerializeField] private float reelSensitivity = 1f;

    private float currentRopeLength = 0f;
    private float previousAngle;

    private void Start()
    {
        previousAngle = reel.localEulerAngles.z;

        currentRopeLength = minRopeLength;

        ropeEnd.position = originalHookPosition.position;
    }

    private void Update()
    {
        float currentAngle = reel.localEulerAngles.z;

        // Handles wrapping from 359 -> 0 and vice versa
        float deltaAngle = Mathf.DeltaAngle(previousAngle, currentAngle);

        previousAngle = currentAngle;

        // Convert reel rotation to rope length
        float ropeDelta = (deltaAngle / 360f) * reelSensitivity;

        currentRopeLength += ropeDelta;

        currentRopeLength = Mathf.Clamp(
            currentRopeLength,
            minRopeLength,
            maxRopeLength
        );

        Vector3 pos = originalHookPosition.position;

        // Rope goes upward because setup is upside down
        pos.y -= currentRopeLength;

        ropeEnd.position = pos;
    }

    public float CurrentRopeLength => currentRopeLength;
}