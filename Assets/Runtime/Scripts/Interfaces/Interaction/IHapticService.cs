namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for XR controller haptics. Injected in place of
    /// <c>HapticFeedback.Singleton</c>. Backed by the scene-scoped <see cref="HapticFeedback"/>
    /// (scene-scoped because it references the active rig's controller impulse players).
    /// </summary>
    public interface IHapticService
    {
        void TriggerVibration(HapticProfileSO profile, HapticHand targetHand);
        void TriggerVibration(float amplitude, float duration, float frequency, HapticHand targetHand);
        void StartContinuousVibration(float amplitude, float frequency, HapticHand targetHand);
        void StopContinuousVibration(HapticHand targetHand);
    }
}
