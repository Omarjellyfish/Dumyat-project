using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "New Haptic Profile", menuName = "Dumyat/Haptics/Haptic Profile")]
    public class HapticProfileSO : ScriptableObject
    {
        [Range(0f, 1f), Tooltip("Strength of the vibration from 0 to 1.")]
        public float amplitude = 0.5f;

        [Tooltip("How long the vibration lasts in seconds.")]
        public float duration = 0.1f;

        [Tooltip("The frequency of the vibration.")]
        public float frequency = 0.5f;
    }
}
