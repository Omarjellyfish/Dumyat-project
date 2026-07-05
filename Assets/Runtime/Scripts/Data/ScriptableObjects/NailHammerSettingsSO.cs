using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "New Nail Hammer Settings", menuName = "Dumyat/Tools/Nail Hammer Settings")]
    public class NailHammerSettingsSO : ScriptableObject
    {
        [Header("Impact Detection")]
        [Tooltip("Minimum hammer speed (m/s) to register a valid strike.")]
        public float minImpactVelocity = 2.0f;

        [Tooltip("Seconds after a successful strike before another can register.")]
        public float impactCooldown = 0.3f;

        [Header("Raycast")]
        [Tooltip("How far (m) to raycast from the contact point to find the second plank.")]
        public float raycastDistance = 0.15f;

        [Header("Joint Settings")]
        [Tooltip("Break force for the FixedJoint. Infinity = unbreakable.")]
        public float jointBreakForce = Mathf.Infinity;

        [Tooltip("Break torque for the FixedJoint. Infinity = unbreakable.")]
        public float jointBreakTorque = Mathf.Infinity;

        [Header("Visuals")]
        [Tooltip("Cosmetic nail prefab (no collider/rigidbody). Spawned at the impact point.")]
        public GameObject nailPrefab;

        [Header("Audio")]
        [Tooltip("Randomized nail-in sound clips played when attaching.")]
        public AudioClip[] nailInClips;

        [Tooltip("Randomized nail-out/removal sound clips played when detaching.")]
        public AudioClip[] nailOutClips;

        [Range(0f, 1f)]
        public float sfxVolume = 0.8f;

        [Header("Haptics")]
        [Tooltip("Haptic profile for every general hit that reaches minimum velocity.")]
        public HapticProfileSO generalImpactHapticProfile;

        [Tooltip("Haptic profile for the grabbing hand when nailing in.")]
        public HapticProfileSO nailInHapticProfile;

        [Tooltip("Haptic profile for the grabbing hand when pulling a nail out.")]
        public HapticProfileSO nailOutHapticProfile;
    }
}
