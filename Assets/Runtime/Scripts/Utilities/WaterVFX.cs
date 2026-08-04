using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DumyatRuntime
{
    [ExecuteAlways]
    public class WaterVFX : MonoBehaviour {

        [Header("Water Settings Preset")]
        [Tooltip("Assign a WaterSettingsSO asset here.")]
        public WaterSettingsSO settings;

        [Tooltip("If assigned, this existing material will be used directly.")]
        public Material existingMaterial;

        [System.Serializable]
        public struct WaveData {
            public float amplitude;
            public float frequency;
            public float phase;
            public float pad;
            public Vector2 direction;
        }

        private Material waterMaterial;
        public ComputeBuffer WaveBuffer => waveBuffer;
        private ComputeBuffer waveBuffer;
        private WaveData[] waves;

        void OnEnable() {
            CreateMaterial();
            CreateWaveBuffer();
            UpdateWaves();
        }

        void OnWillRenderObject() {
            EnsureValidAndBind();
        }

        void Update() {
            EnsureValidAndBind();

            if (transform.hasChanged) {
                UpdateWaves();
                transform.hasChanged = false;
            }
        }

        private void OnValidate() {
            #if UNITY_EDITOR
            EditorApplication.delayCall -= SafeUpdateWaves;
            EditorApplication.delayCall += SafeUpdateWaves;
            #endif
        }

        private void SafeUpdateWaves() {
            if (this == null) return;
            EnsureValidAndBind();
            UpdateWaves();
        }

        private void EnsureValidAndBind() {
            if (settings == null) return;

            if (waterMaterial == null && existingMaterial != null) {
                CreateMaterial();
            }

            if (waveBuffer == null || !waveBuffer.IsValid()) {
                CreateWaveBuffer();
                UpdateWaves();
            } else if (waterMaterial != null) {
                waterMaterial.SetBuffer("_Waves", waveBuffer);
                waterMaterial.SetInt("_WaveCount", settings.waveCount);
            }
        }

        [ContextMenu("Force Rebuild Waves")]
        public void ForceRebuildPlane() {
            CreateMaterial();
            CreateWaveBuffer();
            UpdateWaves();
        }

        public void UpdateWaves() {
            if (settings == null || waveBuffer == null || !waveBuffer.IsValid() || waterMaterial == null) return;

            if (settings.imTiredUnityHandleIt) {
                LetUnityHandleIt();
            } else {
                GenerateStaticWaves();
            }

            waveBuffer.SetData(waves);
            waterMaterial.SetBuffer("_Waves", waveBuffer);
            waterMaterial.SetInt("_WaveCount", settings.waveCount);
        }

        private void LetUnityHandleIt() {
            if (settings.liveUpdateProceduralWaves || !settings.proceduralWavesGenerated) {
                RandomizeProceduralWaves(skipPush: true);
            }

            for (int i = 0; i < settings.waveCount; ++i) {
                waves[i] = settings.savedProceduralWaves[i];
            }
        }

        [ContextMenu("Randomize Procedural Waves")]
        public void RandomizeProceduralWaves() => RandomizeProceduralWaves(skipPush: false);

        private void RandomizeProceduralWaves(bool skipPush) {
            if (settings == null) return;

            float wavelengthMin = settings.medianWavelength / (1.0f + settings.wavelengthRange);
            float wavelengthMax = settings.medianWavelength * (1.0f + settings.wavelengthRange);
            float directionMin = settings.medianDirection - settings.directionalRange;
            float directionMax = settings.medianDirection + settings.directionalRange;
            float speedMin = Mathf.Max(0.01f, settings.medianSpeed - settings.speedRange);
            float speedMax = settings.medianSpeed + settings.speedRange;
            float ampOverLen = settings.medianAmplitude / settings.medianWavelength;

            for (int i = 0; i < 16; ++i) {
                float wavelength = UnityEngine.Random.Range(wavelengthMin, wavelengthMax);
                float direction = UnityEngine.Random.Range(directionMin, directionMax);
                float amplitude = wavelength * ampOverLen;
                float speed = UnityEngine.Random.Range(speedMin, speedMax);

                float frequency = 2.0f / wavelength;
                float phase = speed * Mathf.Sqrt(9.8f * 2.0f * Mathf.PI / wavelength);

                float rad = Mathf.Deg2Rad * direction;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;

                settings.savedProceduralWaves[i] = new WaveData {
                    amplitude = amplitude,
                    frequency = frequency,
                    phase = phase,
                    direction = dir
                };
            }

            settings.proceduralWavesGenerated = true;

            #if UNITY_EDITOR
            EditorUtility.SetDirty(settings);
            #endif

            if (!skipPush) {
                UpdateWaves();
            }
        }

        private void GenerateStaticWaves() {
            if (settings == null) return;

            float freq = 2.0f / settings.manualWavelength;
            float amp = settings.manualAmplitude;
            float speed = settings.manualSpeed;
            float dirAngle = settings.manualDirection;

            for (int i = 0; i < settings.waveCount; ++i) {
                float phase = speed * Mathf.Sqrt(9.8f * 2.0f * Mathf.PI / (2.0f / freq));
                float rad = Mathf.Deg2Rad * dirAngle;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;

                waves[i] = new WaveData {
                    amplitude = amp,
                    frequency = freq,
                    phase = phase,
                    direction = dir
                };

                freq *= 1.5f;
                amp *= 0.5f;
                speed *= 1.2f;
                dirAngle += 30.0f;
            }
        }

        private void CreateMaterial() {
        if (existingMaterial == null) {
            Debug.LogWarning("WaterVFX: No material assigned. Assign one in the inspector.", this);
            return;
        }

        waterMaterial = existingMaterial;
        // No MeshRenderer assignment here - this object doesn't need to render
        // anything itself. It just pushes computed wave data into the shared
        // material, which some other object's MeshRenderer is already using.
    }

        private void CreateWaveBuffer() {
            if (waveBuffer != null) {
                waveBuffer.Release();
                waveBuffer = null;
            }

            waves = new WaveData[16];
            waveBuffer = new ComputeBuffer(16, Marshal.SizeOf(typeof(WaveData)));
        }

        void OnDisable() {
            #if UNITY_EDITOR
            EditorApplication.delayCall -= SafeUpdateWaves;
            #endif

            if (waveBuffer != null) {
                waveBuffer.Release();
                waveBuffer = null;
            }
        }
    }
}