using UnityEngine;
using UnityEngine.VFX;

namespace DumyatRuntime
{
    [ExecuteAlways]
    public class SawVFXController : MonoBehaviour
    {
        public VisualEffect sawVFX;

        /// <summary>
        /// Which "mode" the saw is in this frame. Determined purely by acceleration vs.
        /// threshold (and its direction relative to travel) — see DetermineState().
        /// </summary>
        public enum SawState
        {
            Idle,        // below minVelocityToEmit — nothing spawns
            Continuous,  // normal velocity-driven stream, no strong accel in either direction
            Suppressed,  // accelerating hard IN travel direction — sawdust stays trapped
            Bursting     // decelerating hard AGAINST travel direction — trapped dust releases
        }

        [Header("Debug")]
        [Tooltip("READ-ONLY. Shows which state the logic picked this frame. Watch this in Play Mode to confirm burst is actually triggering.")]
        public SawState currentState;
        [Tooltip("READ-ONLY. Current burst intensity (0 = off, 1 = peak). Should spike to 1 the instant currentState shows Bursting, then decay.")]
        public float currentBurstIntensity;

        [Header("Spawn Rate Controls")]
        public float minVelocityToEmit = 0.1f;
        public float maxVelocity = 5.0f;
        public float maxSpawnRate = 500f;
        [Range(0.1f, 100f)] public float spawnSensitivity = 3.0f;

        [Header("Intelligent Burst Settings")]
        [Tooltip("Acceleration magnitude (same direction as travel) above which the state becomes Suppressed.")]
        public float accelerationSuppressionThreshold = 15f;
        [Tooltip("Acceleration magnitude (opposing travel direction) above which the state becomes Bursting.")]
        public float burstTriggerThreshold = 2f;
        [Tooltip("How quickly a triggered burst fades back to normal. Higher = shorter, punchier burst.")]
        [Range(0.5f, 20f)] public float burstDecaySpeed = 6f;

        [Space(10)]
        [Tooltip("Multiplier for the particle count at peak burst intensity.")]
        [Range(1f, 15f)] public float burstSpawnMultiplier = 5.0f;
        [Tooltip("Multiplier for particle speed at peak burst intensity.")]
        [Range(1f, 10f)] public float burstSpeedMultiplier = 3.0f;
        [Tooltip("Stream focus at peak burst intensity. (Does not affect the continuous stream)")]
        [Range(0f, 1f)] public float maxBurstStreamFocus = 0.95f;
        [Tooltip("How much faster properties respond while a burst is active, versus normal easing.")]
        public float burstResponseSpeed = 60f;
        [Tooltip("Spawn rate multiplier while Suppressed (trapped dust).")]
        [Range(0f, 1f)] public float suppressionSpawnMultiplier = 0.1f;

        [Header("Particle Size Controls")]
        public float baseParticleSize = 0.02f;
        public float maxParticleSizeMultiplier = 1.5f;
        [Range(0.1f, 10f)] public float sizeSensitivity = 2.0f;

        [Header("Velocity/Speed Controls")]
        public float baseOverallSpeed = 2.0f;
        public float maxOverallSpeedMultiplier = 2.5f;
        [Range(0.1f, 10f)] public float speedSensitivity = 2.5f;

        [Header("Stream Focus Controls")]
        public float baseStreamFocus = 0.5f;
        public float maxStreamFocus = 0.95f;
        [Range(0.1f, 10f)] public float focusSensitivity = 3.0f;

        [Header("Air Drag Controls")]
        public float baseDrag = 0.1f;
        public float maxDrag = 1.5f;
        [Range(0.1f, 10f)] public float dragSensitivity = 1.5f;

        [Header("Backward Stroke Settings")]
        [Range(0f, 1f)]
        public float backwardIntensityMultiplier = 0.5f;

        [Header("Smoothing")]
        [Tooltip("Smoothing applied ONLY to the acceleration estimate.")]
        [Range(0.01f, 1f)] public float accelerationSmoothing = 0.35f;
        [Tooltip("How quickly output properties chase their target values under normal (non-burst) conditions.")]
        public float propertyLerpSpeed = 30f;

        private struct SawTargets
        {
            public float spawnRate;
            public float size;
            public float overallSpeed;
            public float focus;
            public float drag;
            public float directionSign;
        }

        private Vector3 lastPosition;
        private float lastRawLocalZVelocity;
        private float smoothedAcceleration;
        private float burstIntensity; // 0 = no burst, 1 = peak burst, decays over time

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
            lastRawLocalZVelocity = 0f;
            smoothedAcceleration = 0f;
            burstIntensity = 0f;
            currentState = SawState.Idle;

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

            float rawLocalZVelocity = CalculateRawLocalZVelocity(deltaTime);
            UpdateSmoothedAcceleration(rawLocalZVelocity, deltaTime);

            // 1. Decide the state FIRST — everything else follows from this.
            currentState = DetermineState(rawLocalZVelocity);

            // 2. Burst intensity is driven by state transitions, then decays on its own.
            if (currentState == SawState.Bursting)
            {
                burstIntensity = 1f;
            }
            burstIntensity = Mathf.MoveTowards(burstIntensity, 0f, burstDecaySpeed * deltaTime);
            currentBurstIntensity = burstIntensity; // mirror into Inspector-visible field

            // 3. Apply exactly the logic each state needs.
            SawTargets targets = ComputeBaseTargets(rawLocalZVelocity);
            switch (currentState)
            {
                case SawState.Idle:
                    // Targets already zeroed by ComputeBaseTargets — nothing further to do.
                    break;

                case SawState.Continuous:
                    // Plain velocity-driven output, no accel-based modifier.
                    break;

                case SawState.Suppressed:
                    targets.spawnRate *= suppressionSpawnMultiplier;
                    break;

                case SawState.Bursting:
                    targets.spawnRate *= Mathf.Lerp(1f, burstSpawnMultiplier, burstIntensity);
                    targets.overallSpeed *= Mathf.Lerp(1f, burstSpeedMultiplier, burstIntensity);
                    targets.focus = Mathf.Lerp(targets.focus, maxBurstStreamFocus, burstIntensity);
                    break;
            }

            // Backward-stroke damping is about direction, not state, so it applies on top
            // of whichever state's targets were just computed.
            ApplyBackwardStrokeDamping(ref targets, rawLocalZVelocity);

            SmoothTowardTargets(targets, deltaTime);
            PushPropertiesToVFX();

            lastPosition = transform.position;
            lastRawLocalZVelocity = rawLocalZVelocity;
        }

        private float CalculateRawLocalZVelocity(float deltaTime)
        {
            Vector3 worldDelta = transform.position - lastPosition;
            Vector3 localDelta = transform.InverseTransformDirection(worldDelta);
            return localDelta.z / deltaTime;
        }

        private void UpdateSmoothedAcceleration(float rawLocalZVelocity, float deltaTime)
        {
            float rawAcceleration = (rawLocalZVelocity - lastRawLocalZVelocity) / deltaTime;
            smoothedAcceleration = Mathf.Lerp(smoothedAcceleration, rawAcceleration, accelerationSmoothing);
        }

        /// <summary>
        /// The single place that decides which state we're in. Priority order matters:
        /// Idle beats everything, then Suppressed/Bursting are both "high acceleration"
        /// cases distinguished only by direction, and Continuous is the fallback.
        /// </summary>
        private SawState DetermineState(float rawLocalZVelocity)
        {
            float speedOnLocalZ = Mathf.Abs(rawLocalZVelocity);
            if (speedOnLocalZ <= minVelocityToEmit)
            {
                return SawState.Idle;
            }

            float travelDirection = Mathf.Sign(rawLocalZVelocity);
            float accelDirection = Mathf.Sign(smoothedAcceleration);
            float accelMagnitude = Mathf.Abs(smoothedAcceleration);
            bool accelOpposesTravel = accelDirection != travelDirection;

            if (accelOpposesTravel && accelMagnitude > burstTriggerThreshold)
            {
                return SawState.Bursting;
            }

            if (!accelOpposesTravel && accelMagnitude > accelerationSuppressionThreshold)
            {
                return SawState.Suppressed;
            }

            return SawState.Continuous;
        }

        /// <summary>
        /// Pure velocity-driven targets — the same for Idle/Continuous/Suppressed/Bursting.
        /// State-specific modifiers are applied afterward in Update().
        /// </summary>
        private SawTargets ComputeBaseTargets(float rawLocalZVelocity)
        {
            var targets = new SawTargets
            {
                spawnRate = 0f,
                size = 0f,
                overallSpeed = 0f,
                focus = baseStreamFocus,
                drag = baseDrag,
                directionSign = currentDirectionSign
            };

            float speedOnLocalZ = Mathf.Abs(rawLocalZVelocity);
            if (speedOnLocalZ <= minVelocityToEmit)
            {
                return targets;
            }

            float spawnGrowth = Mathf.Clamp01((speedOnLocalZ * spawnSensitivity) / maxVelocity);
            float sizeGrowth = Mathf.Clamp01(speedOnLocalZ * sizeSensitivity);
            float speedGrowth = Mathf.Clamp01(speedOnLocalZ * speedSensitivity);
            float focusGrowth = Mathf.Clamp01(speedOnLocalZ * focusSensitivity);
            float dragGrowth = Mathf.Clamp01(speedOnLocalZ * dragSensitivity);

            targets.spawnRate = spawnGrowth * maxSpawnRate;
            targets.size = baseParticleSize * Mathf.Lerp(1.0f, maxParticleSizeMultiplier, sizeGrowth);
            targets.overallSpeed = baseOverallSpeed * Mathf.Lerp(1.0f, maxOverallSpeedMultiplier, speedGrowth);
            targets.focus = Mathf.Lerp(baseStreamFocus, maxStreamFocus, focusGrowth);
            targets.drag = Mathf.Lerp(baseDrag, maxDrag, dragGrowth);
            targets.directionSign = rawLocalZVelocity < 0 ? -1f : 1f;

            return targets;
        }

        private void ApplyBackwardStrokeDamping(ref SawTargets targets, float rawLocalZVelocity)
        {
            if (rawLocalZVelocity < 0)
            {
                targets.spawnRate *= backwardIntensityMultiplier;
                targets.size *= backwardIntensityMultiplier;
                targets.overallSpeed *= backwardIntensityMultiplier;
            }
        }

        private void SmoothTowardTargets(SawTargets targets, float deltaTime)
        {
            float normalLerpFactor = 1f - Mathf.Exp(-propertyLerpSpeed * deltaTime);
            float burstLerpFactor = 1f - Mathf.Exp(-burstResponseSpeed * deltaTime);
            float spawnSpeedFocusLerpFactor = Mathf.Lerp(normalLerpFactor, burstLerpFactor, burstIntensity);

            currentSpawnRate = Mathf.Lerp(currentSpawnRate, targets.spawnRate, spawnSpeedFocusLerpFactor);
            currentOverallSpeed = Mathf.Lerp(currentOverallSpeed, targets.overallSpeed, spawnSpeedFocusLerpFactor);
            currentFocus = Mathf.Lerp(currentFocus, targets.focus, spawnSpeedFocusLerpFactor);

            currentSize = Mathf.Lerp(currentSize, targets.size, normalLerpFactor);
            currentDrag = Mathf.Lerp(currentDrag, targets.drag, normalLerpFactor);
            currentDirectionSign = targets.directionSign;
        }

        private void PushPropertiesToVFX()
        {
            if (sawVFX == null) return;

            sawVFX.SetFloat(ID_SpawnRate, currentSpawnRate);
            sawVFX.SetFloat(ID_EjectionDirectionZ, currentDirectionSign);
            sawVFX.SetFloat(ID_ParticleSize, currentSize);
            sawVFX.SetFloat(ID_OverallSpeed, currentOverallSpeed);
            sawVFX.SetFloat(ID_StreamFocus, currentFocus);
            sawVFX.SetFloat(ID_DragCoefficient, currentDrag);
        }
    }
}