using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DumyatRuntime
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [ExecuteAlways] // Allows Update to run in Edit Mode so we see real-time updates!
    public class WaterVFX : MonoBehaviour {
        public Shader waterShader;

        [Header("Mesh Settings")]
        [Tooltip("If enabled, changing plane settings below will instantly regenerate the mesh in the scene view.")]
        public bool realTimeMeshUpdates = true;

        [Tooltip("The physical width and length of the generated water plane in world units.")]
        public int planeLength = 10;
        
        [Tooltip("The density of the grid. Higher resolution means smoother wave curves, but more vertices for the GPU to process.")]
        public int quadRes = 10;

        // Struct size matches exactly what HLSL expects
        public struct WaveData {
            public float amplitude;
            public float frequency;
            public float phase;
            public float pad; // Kept for 16-byte structure alignment on GPU
            public Vector2 direction;
        }

        [Header("Generation Settings")]
        [Tooltip("When enabled, Unity automatically generates random waves using the FBM procedural rules. When disabled, it falls back to a deterministic cascading set of manual waves.")]
        public bool imTiredUnityHandleIt = true;
        
        [Range(1, 16)] 
        [Tooltip("How many wave layers to calculate on the GPU. Max is 16 for optimal mobile/Quest performance.")]
        public int waveCount = 8;

        [Header("Procedural (I'm Tired) Settings")]
        [Tooltip("The middle wavelength around which procedural waves will be randomized. Larger wavelengths create broader, lazy swells.")]
        public float medianWavelength = 4.0f;
        
        [Tooltip("The range (+/-) allowed for randomizing wavelengths. E.g., 1.5 allows waves to be significantly larger or smaller than the median.")]
        public float wavelengthRange = 1.5f;
        
        [Tooltip("The default height scale of the waves. This is automatically scaled relative to each randomized wavelength to prevent unrealistic steepness.")]
        public float medianAmplitude = 0.3f;
        
        [Tooltip("The base speed of wave movement across the water surface.")]
        public float medianSpeed = 1.5f;
        
        [Tooltip("The maximum randomized speed deviation added to or subtracted from the median speed.")]
        public float speedRange = 0.5f;
        
        [Tooltip("The average heading direction of the waves in degrees (0 to 360).")]
        public float medianDirection = 0.0f;
        
        [Tooltip("The maximum angle spread allowed on either side of the median direction to create realistic 'choppy' cross-currents.")]
        public float directionalRange = 45.0f;

        [Header("Static Manual Waves (Fine, I'll Do It Myself)")]
        [Tooltip("The base movement heading direction for the first manual wave octave.")]
        public float manualDirection = 0.0f;
        
        [Tooltip("The starting wavelength for the first manual octave. Subsequent octaves will progressively shrink in size.")]
        public float manualWavelength = 3.0f;
        
        [Tooltip("The peak wave height for the first manual octave. Subsequent octaves will be scaled down.")]
        public float manualAmplitude = 0.2f;
        
        [Tooltip("The base travel speed for the manual wave sequence.")]
        public float manualSpeed = 1.0f;

        private Mesh mesh;
        private Material waterMaterial;
        private ComputeBuffer waveBuffer;
        private WaveData[] waves;
        private bool meshNeedsRebuild = false;

        void OnEnable() {
            CreateWaterPlane();
            CreateMaterial();
            CreateWaveBuffer();
            UpdateWaves();
        }

        void Update() {
            // Tweak wave settings in the inspector to see real-time wave updates
            if (transform.hasChanged) {
                UpdateWaves();
                transform.hasChanged = false;
            }
        }

        // Detects inspector changes safely in real time!
        private void OnValidate() {
            // Clamp values to prevent crashing Unity with infinite grids
            planeLength = Mathf.Max(1, planeLength);
            quadRes = Mathf.Max(1, quadRes);

            if (realTimeMeshUpdates) {
                meshNeedsRebuild = true;
                
                #if UNITY_EDITOR
                EditorApplication.delayCall -= SafeRebuildMesh;
                EditorApplication.delayCall += SafeRebuildMesh;
                #endif
            }

            // NEW: push wave parameter changes to the GPU immediately
            #if UNITY_EDITOR
            EditorApplication.delayCall -= SafeUpdateWaves;
            EditorApplication.delayCall += SafeUpdateWaves;
            #endif
        }

        private void SafeUpdateWaves() {
            if (this == null || waveBuffer == null || waterMaterial == null) return;
            UpdateWaves();
        }

        private void SafeRebuildMesh() {
            if (!meshNeedsRebuild || this == null) return;
            
            CreateWaterPlane();
            meshNeedsRebuild = false;
        }

        [ContextMenu("Force Rebuild Plane")]
        public void ForceRebuildPlane() {
            CreateWaterPlane();
            CreateMaterial();
            CreateWaveBuffer();
            UpdateWaves();
        }

        public void ToggleTiredMode() {
            if (!Application.isPlaying) {
                Debug.Log("Can't complain yet, we aren't even in Play Mode!");
                return;
            }
            imTiredUnityHandleIt = !imTiredUnityHandleIt;
            UpdateWaves();
        }

        public void UpdateWaves() {
            if (waveBuffer == null || waterMaterial == null) return;

            if (imTiredUnityHandleIt) {
                LetUnityHandleIt();
            } else {
                GenerateStaticWaves();
            }

            waveBuffer.SetData(waves);
            waterMaterial.SetBuffer("_Waves", waveBuffer);
            waterMaterial.SetInt("_WaveCount", waveCount);
        }

        private void LetUnityHandleIt() {
            float wavelengthMin = medianWavelength / (1.0f + wavelengthRange);
            float wavelengthMax = medianWavelength * (1.0f + wavelengthRange);
            float directionMin = medianDirection - directionalRange;
            float directionMax = medianDirection + directionalRange;
            float speedMin = Mathf.Max(0.01f, medianSpeed - speedRange);
            float speedMax = medianSpeed + speedRange;
            float ampOverLen = medianAmplitude / medianWavelength;

            for (int i = 0; i < waveCount; ++i) {
                float wavelength = UnityEngine.Random.Range(wavelengthMin, wavelengthMax);
                float direction = UnityEngine.Random.Range(directionMin, directionMax);
                float amplitude = wavelength * ampOverLen;
                float speed = UnityEngine.Random.Range(speedMin, speedMax);

                float frequency = 2.0f / wavelength;
                // Physical deep water dispersion relation
                float phase = speed * Mathf.Sqrt(9.8f * 2.0f * Mathf.PI / wavelength);
                
                float rad = Mathf.Deg2Rad * direction;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;

                waves[i] = new WaveData {
                    amplitude = amplitude,
                    frequency = frequency,
                    phase = phase,
                    direction = dir
                };
            }
        }

        private void GenerateStaticWaves() {
            // Creates a cascading set of manual octaves
            float freq = 2.0f / manualWavelength;
            float amp = manualAmplitude;
            float speed = manualSpeed;
            float dirAngle = manualDirection;

            for (int i = 0; i < waveCount; ++i) {
                float phase = speed * Mathf.Sqrt(9.8f * 2.0f * Mathf.PI / (2.0f / freq));
                float rad = Mathf.Deg2Rad * dirAngle;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;

                waves[i] = new WaveData {
                    amplitude = amp,
                    frequency = freq,
                    phase = phase,
                    direction = dir
                };

                // Progressive octave shifting
                freq *= 1.5f;
                amp *= 0.5f;
                speed *= 1.2f;
                dirAngle += 30.0f; // Shift angle slightly for cross-chop
            }
        }

        private void CreateWaterPlane() {
            GetComponent<MeshFilter>().mesh = mesh = new Mesh();
            mesh.name = "WaterPlane";
            mesh.indexFormat = IndexFormat.UInt32;

            float halfLength = planeLength * 0.5f;
            int sideVertCount = planeLength * quadRes;

            Vector3[] vertices = new Vector3[(sideVertCount + 1) * (sideVertCount + 1)];
            Vector2[] uv = new Vector2[vertices.Length];

            for (int i = 0, x = 0; x <= sideVertCount; ++x) {
                for (int z = 0; z <= sideVertCount; ++z, ++i) {
                    vertices[i] = new Vector3(
                        ((float)x / sideVertCount * planeLength) - halfLength, 
                        0, 
                        ((float)z / sideVertCount * planeLength) - halfLength
                    );
                    uv[i] = new Vector2((float)x / sideVertCount, (float)z / sideVertCount);
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uv;

            int[] triangles = new int[sideVertCount * sideVertCount * 6];
            for (int ti = 0, vi = 0, x = 0; x < sideVertCount; ++vi, ++x) {
                for (int z = 0; z < sideVertCount; ti += 6, ++vi, ++z) {
                    triangles[ti] = vi;
                    triangles[ti + 1] = vi + 1;
                    triangles[ti + 2] = vi + sideVertCount + 2;
                    triangles[ti + 3] = vi;
                    triangles[ti + 4] = vi + sideVertCount + 2;
                    triangles[ti + 5] = vi + sideVertCount + 1;
                }
            }

            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
        }

        private void CreateMaterial() {
            if (waterShader == null) return;
            waterMaterial = new Material(waterShader);
            GetComponent<MeshRenderer>().material = waterMaterial;
        }

        private void CreateWaveBuffer() {
            waves = new WaveData[16]; // Hard cap of 16 wave layers (perfect for mobile)
            waveBuffer = new ComputeBuffer(16, Marshal.SizeOf(typeof(WaveData)));
        }

        void OnDisable() {
            #if UNITY_EDITOR
            EditorApplication.delayCall -= SafeRebuildMesh;
            #endif
            
            if (waterMaterial != null) DestroyImmediate(waterMaterial);
            if (mesh != null) DestroyImmediate(mesh);
            if (waveBuffer != null) {
                waveBuffer.Release();
                waveBuffer = null;
            }
        }
    }
}