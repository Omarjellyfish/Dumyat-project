using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "New Saw Settings", menuName = "Dumyat/Tools/Saw Settings")]
    public class SawSettingsSO : ScriptableObject
    {
        [Header("Cutting Mechanics")]
        [Tooltip("Minimum distance the saw must move in one continuous direction to count as a stroke.")]
        public float minimumMoveDistance = 0.005f;

        [Tooltip("Number of continuous back-and-forth strokes required to cut through a plank.")]
        public int strokesNeededToCut = 15;

        [Header("Audio")]
        [Tooltip("Randomized sound clips played on every sawing stroke.")]
        public AudioClip[] sawStrokeClips;

        [Tooltip("Randomized sound clips played when the cut is successfully completed.")]
        public AudioClip[] cutCompleteClips;

        [Range(0f, 1f)]
        public float sfxVolume = 0.8f;
    }
}
