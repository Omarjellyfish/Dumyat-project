using UnityEngine;
using UnityEngine.VFX;

namespace DumyatRuntime
{
    [ExecuteAlways]
    public class SawVFXController : MonoBehaviour
    {
        public VisualEffect sawVFX;

        [Header("Spawn Rate Controls")]
        public float minVelocityToEmit = 0.1f;
        public float maxVelocity = 5.0f;
        public float maxSpawnRate = 500f;
        [Tooltip("Higher values make the spawn rate reach maximum faster at lower speeds.")]
        [Range(0.1f, 10f)] public float spawnSensitivity = 1.0f;

        [Header("Particle Size Controls")]
        public float baseParticleSize = 0.02f;
        public float maxParticleSizeMultiplier = 1.5f;
        [Tooltip("Higher values make the size reach maximum faster at lower speeds.")]
        [Range(0.1f, 10f)] public float sizeSensitivity = 1.0f;

        [Header("Velocity/Speed Controls")]
        public float baseOverallSpeed = 2.0f;
        public float maxOverallSpeedMultiplier = 2.5f;
        [Tooltip("Higher values make the particle speed reach maximum faster at lower speeds.")]
        [Range(0.1f, 10f)] public float speedSensitivity = 1.0f;

        [Header("Stream Focus Controls")]
        public float baseStreamFocus = 0.5f;
        public float maxStreamFocus = 0.95f;
        [Tooltip("Higher values make the stream focus reach maximum faster at lower speeds.")]
        [Range(0.1f, 10f)] public float focusSensitivity = 1.0f;

        [Header("Air Drag Controls")]
        public float baseDrag = 0.1f;
        public float maxDrag = 1.5f;
        [Tooltip("Higher values make the drag reach maximum faster at lower speeds.")]
        [Range(0.1f, 10f)] public float dragSensitivity = 1.5f;

        [Header("Backward Stroke Settings")]
        [Range(0f, 1f)]
        public float backwardIntensityMultiplier = 0.5f;

        [Header("Smoothing")]
        [Tooltip("How quickly output properties chase their target values.")]
        public float propertyLerpSpeed = 30f;

        private Vector3 lastPosition;

        private float currentSpawnRate;
        private float currentSize;
        private float currentOverallSpeed;
        private float currentFocus;
        private float currentDrag;
        private float currentDirectionSign = 1f;

        private static readonly int ID_SpawnRate = Shader.PropertyToID("SpawnRate");
        private static readonly int ID_EjectionDirectionZ = Shader.PropertyToID("EjectionDirectionZ");
        private static readonly int ID_ParticleSize = Shader.PropertyToID("ParticleSize");
        private static readonly int ID_OverallSpeed = Shader.PropertyToID("Overall Speed");
        private static readonly int ID_StreamFocus = Shader.PropertyToID("Stream Focus");
        private static readonly int ID_DragCoefficient = Shader.PropertyToID("Drag Coefficient");

        void OnEnable()
        {
            lastPosition = transform.position;

            currentSpawnRate = 0f;
            currentSize = baseParticleSize;
            currentOverallSpeed = baseOverallSpeed;
            currentFocus = baseStreamFocus;
            currentDrag = baseDrag;
            currentDirectionSign = 1f;
        }

        void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            // 1. Calculate raw local Z velocity
            Vector3 worldDelta = transform.position - lastPosition;
            Vector3 localDelta = transform.InverseTransformDirection(worldDelta);
            float rawLocalZVelocity = localDelta.z / deltaTime;
            float speedOnLocalZ = Mathf.Abs(rawLocalZVelocity);

            // 2. Compute Targets based entirely on continuous velocity
            float targetSpawnRate = 0f;
            float targetDirectionSign = currentDirectionSign;
            float targetSize = 0f;
            float targetOverallSpeed = 0f;
            float targetFocus = baseStreamFocus;
            float targetDrag = baseDrag;

            if (speedOnLocalZ > minVelocityToEmit)
            {
                // Normalize current speed to a perfect 0.0 - 1.0 range based on maxVelocity
                float normalizedSpeed = Mathf.Clamp01(speedOnLocalZ / maxVelocity);

                // Curve calculation using your sensitivity curves
                float spawnGrowth = Mathf.Pow(normalizedSpeed, 1f / Mathf.Max(0.01f, spawnSensitivity));
                float sizeGrowth  = Mathf.Pow(normalizedSpeed, 1f / Mathf.Max(0.01f, sizeSensitivity));
                float speedGrowth = Mathf.Pow(normalizedSpeed, 1f / Mathf.Max(0.01f, speedSensitivity));
                float focusGrowth = Mathf.Pow(normalizedSpeed, 1f / Mathf.Max(0.01f, focusSensitivity));
                float dragGrowth  = Mathf.Pow(normalizedSpeed, 1f / Mathf.Max(0.01f, dragSensitivity));

                // Map safely to baseline properties
                targetSpawnRate = spawnGrowth * maxSpawnRate;
                targetSize = baseParticleSize * Mathf.Lerp(1.0f, maxParticleSizeMultiplier, sizeGrowth);
                targetOverallSpeed = baseOverallSpeed * Mathf.Lerp(1.0f, maxOverallSpeedMultiplier, speedGrowth);
                targetFocus = Mathf.Lerp(baseStreamFocus, maxStreamFocus, focusGrowth);
                targetDrag = Mathf.Lerp(baseDrag, maxDrag, dragGrowth);
                targetDirectionSign = rawLocalZVelocity < 0 ? -1f : 1f;

                // Apply backward stroke scaling if cutting backward
                if (rawLocalZVelocity < 0)
                {
                    targetSpawnRate *= backwardIntensityMultiplier;
                    targetSize *= backwardIntensityMultiplier;
                    targetOverallSpeed *= backwardIntensityMultiplier;
                }
            }

            // 3. Smooth and Lerp Properties
            float lerpFactor = 1f - Mathf.Exp(-propertyLerpSpeed * deltaTime);
            currentSpawnRate = Mathf.Lerp(currentSpawnRate, targetSpawnRate, lerpFactor);
            currentSize = Mathf.Lerp(currentSize, targetSize, lerpFactor);
            currentOverallSpeed = Mathf.Lerp(currentOverallSpeed, targetOverallSpeed, lerpFactor);
            currentFocus = Mathf.Lerp(currentFocus, targetFocus, lerpFactor);
            currentDrag = Mathf.Lerp(currentDrag, targetDrag, lerpFactor);
            currentDirectionSign = targetDirectionSign;

            // 4. Set Visual Effect Properties
            if (sawVFX != null)
            {
                sawVFX.SetFloat(ID_SpawnRate, currentSpawnRate);
                sawVFX.SetFloat(ID_EjectionDirectionZ, currentDirectionSign);
                sawVFX.SetFloat(ID_ParticleSize, currentSize);
                sawVFX.SetFloat(ID_OverallSpeed, currentOverallSpeed);
                sawVFX.SetFloat(ID_StreamFocus, currentFocus);
                sawVFX.SetFloat(ID_DragCoefficient, currentDrag);
            }

            lastPosition = transform.position;
        }
    }
}