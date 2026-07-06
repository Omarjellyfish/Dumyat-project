using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace DumyatRuntime
{
    public enum HapticHand
    {
        Left,
        Right,
        Both
    }

    public class HapticFeedback : MonoBehaviour, IHapticService
    {
        // Dynamically discovered when needed
        [SerializeField] private HapticImpulsePlayer _leftControllerHaptic;
        [SerializeField] private HapticImpulsePlayer _rightControllerHaptic;

        private void EnsureControllersFound()
        {
            // If controllers are already found and still exist in the scene, do nothing
            if (_leftControllerHaptic != null && _rightControllerHaptic != null) return;

            // Find all haptic players in the scene
            var players = Object.FindObjectsByType<HapticImpulsePlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var player in players)
            {
                string objName = player.gameObject.name.ToLower();
                if (objName.Contains("left"))
                    _leftControllerHaptic = player;
                else if (objName.Contains("right"))
                    _rightControllerHaptic = player;
            }
        }

        #region Public API: Impulse

        public void TriggerVibration(HapticProfileSO profile, HapticHand targetHand)
        {
            if (profile == null)
            {
                Debug.LogWarning("HapticProfileSO is null.using default vibration.");

                // Default values if profile is null
                TriggerVibration(0.5f, 0.1f, 0.5f, targetHand);
            }
            else
            {

                TriggerVibration(profile.amplitude, profile.duration, profile.frequency, targetHand);
            }
        }

        public void TriggerVibration(float amplitude, float duration, float frequency, HapticHand targetHand)
        {
            EnsureControllersFound();

            if (targetHand == HapticHand.Left || targetHand == HapticHand.Both)
            {
                if (_leftControllerHaptic != null)
                    _leftControllerHaptic.SendHapticImpulse(amplitude, duration, frequency);
            }

            if (targetHand == HapticHand.Right || targetHand == HapticHand.Both)
            {
                if (_rightControllerHaptic != null)
                    _rightControllerHaptic.SendHapticImpulse(amplitude, duration, frequency);
            }
        }

        #endregion

        #region Public API: Continuous

        // Use this for prolonged actions like sawing or reeling in a fish
        public void StartContinuousVibration(float amplitude, float frequency, HapticHand targetHand)
        {


            //its a long duration that we iinterrupt with another
            float continuousDuration = 999f;
            TriggerVibration(amplitude, continuousDuration, frequency, targetHand);
        }

        public void StopContinuousVibration(HapticHand targetHand)
        {
            TriggerVibration(0f, 0f, 0f, targetHand);
        }

        #endregion
    }
}