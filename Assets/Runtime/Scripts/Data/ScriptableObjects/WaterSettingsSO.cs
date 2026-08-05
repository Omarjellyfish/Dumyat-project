using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "NewWaterSettings", menuName = "Dumyat/Water Settings")]
    public class WaterSettingsSO : ScriptableObject
    {
        [Header("Generation Settings")]
        [Tooltip("When enabled, Unity automatically generates random waves using the procedural rules.")]
        public bool imTiredUnityHandleIt = true;

        [Range(1, 16)]
        [Tooltip("How many wave layers to calculate on the GPU.")]
        public int waveCount = 8;

        [Header("Procedural Settings")]
        [Tooltip("When enabled, changing parameters re-randomizes the waves live.")]
        public bool liveUpdateProceduralWaves = true;

        [Tooltip("The middle wavelength around which procedural waves will be randomized.")]
        public float medianWavelength = 4.0f;

        [Tooltip("The range (+/-) allowed for randomizing wavelengths.")]
        public float wavelengthRange = 1.5f;

        [Tooltip("The default height scale of the waves.")]
        public float medianAmplitude = 0.3f;

        [Tooltip("The base speed of wave movement across the water surface.")]
        public float medianSpeed = 1.5f;

        [Tooltip("The maximum randomized speed deviation added to or subtracted from median speed.")]
        public float speedRange = 0.5f;

        [Tooltip("The average heading direction of the waves in degrees (0 to 360).")]
        public float medianDirection = 0.0f;

        [Tooltip("The maximum angle spread allowed on either side of median direction.")]
        public float directionalRange = 45.0f;

        [Header("Static Manual Waves")]
        public float manualDirection = 0.0f;
        public float manualWavelength = 3.0f;
        public float manualAmplitude = 0.2f;
        public float manualSpeed = 1.0f;

        [HideInInspector] public WaterVFX.WaveData[] savedProceduralWaves = new WaterVFX.WaveData[16];
        [HideInInspector] public bool proceduralWavesGenerated = false;
        

        private void OnValidate() {
            // Notify active scene listeners when settings are edited in Inspector
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () => {
                WaterVFX[] instances = FindObjectsByType<WaterVFX>(FindObjectsSortMode.None);
                foreach (var water in instances) {
                     water.UpdateWaves();
                    // if (water != null && water.settings == this) {
                    //     water.UpdateWaves();
                    // }
                }
            };
            #endif
        }
    }
}