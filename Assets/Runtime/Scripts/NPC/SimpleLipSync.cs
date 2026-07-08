using System.Text;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Amplitude-driven mouth movement ("lip flap") for a speaking character — deliberately NOT
    /// phoneme-accurate. While the character's <see cref="AudioSource"/> is playing (e.g. the
    /// merchant's TTS line), the mouth blendshape opens in proportion to the loudness of the
    /// audio each frame, which naturally flutters with speech; when playback stops the mouth
    /// eases shut. Works out of the box with Ready Player Me / Wolf3D avatars (they ship
    /// viseme_aa / jawOpen / mouthOpen blendshapes) and with any other rig via the candidate
    /// name list. Self-contained: no packages, no events — it only watches the AudioSource.
    /// </summary>
    public class SimpleLipSync : MonoBehaviour
    {
        [Tooltip("Audio source the character speaks through (the merchant TTS source). " +
                 "Auto-resolved from this GameObject when empty.")]
        [SerializeField] private AudioSource _audioSource;

        [Tooltip("Renderer holding the mouth blendshape. Auto-resolved from children when empty " +
                 "(the first renderer that has one of the candidate blendshapes).")]
        [SerializeField] private SkinnedMeshRenderer _face;

        [Tooltip("Blendshape names tried in order; the first one present on the mesh is driven. " +
                 "Defaults cover Ready Player Me (viseme_aa), ARKit (jawOpen) and Wolf3D (mouthOpen).")]
        [SerializeField] private string[] _blendShapeCandidates = { "viseme_aa", "jawOpen", "mouthOpen" };

        [Tooltip("Loudness-to-openness gain. Raise if the mouth barely moves, lower if it slams open.")]
        [SerializeField, Min(0.1f)] private float _gain = 6f;

        [Tooltip("Maximum blendshape weight (0-100) at full loudness — full 100 usually looks unhinged.")]
        [SerializeField, Range(0f, 100f)] private float _maxWeight = 70f;

        [Tooltip("How fast the mouth opens toward the target (per second, in 0-1 openness units).")]
        [SerializeField, Min(0.1f)] private float _attackSpeed = 14f;

        [Tooltip("How fast the mouth closes when the audio quietens or stops.")]
        [SerializeField, Min(0.1f)] private float _releaseSpeed = 8f;

        private readonly float[] _samples = new float[256];
        private int _blendShapeIndex = -1;
        private float _openness; // current 0..1 smoothed openness

        private void Start()
        {
            if (_audioSource == null) _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
                Debug.LogWarning($"SimpleLipSync: no AudioSource on '{name}'; lip sync disabled.");

            ResolveFace();

            if (_audioSource == null || _blendShapeIndex < 0) enabled = false;
        }

        // LateUpdate so the weight lands after any Animator evaluation this frame.
        private void LateUpdate()
        {
            float target = 0f;
            if (_audioSource.isPlaying)
                target = Mathf.Clamp01(ReadSourceRms() * _gain);

            float speed = target > _openness ? _attackSpeed : _releaseSpeed;
            _openness = Mathf.MoveTowards(_openness, target, speed * Time.deltaTime);
            _face.SetBlendShapeWeight(_blendShapeIndex, _openness * _maxWeight);
        }

        // Reads the CLIP's raw samples at the playhead when possible, so mouth movement is
        // independent of 3D spatialization — AudioSource.GetOutputData returns POST-attenuation
        // output, which collapses to near-zero when the AudioListener is far from the speaker
        // (a full-volume line at 17m reads ~0.02 RMS: the mouth never visibly opens). Falls back
        // to GetOutputData for clips whose raw data isn't readable (compressed/streaming).
        private float ReadSourceRms()
        {
            AudioClip clip = _audioSource.clip;
            if (clip != null && clip.loadType == AudioClipLoadType.DecompressOnLoad)
            {
                int framesNeeded = _samples.Length / Mathf.Max(1, clip.channels);
                int pos = Mathf.Clamp(_audioSource.timeSamples, 0, Mathf.Max(0, clip.samples - framesNeeded - 1));
                if (clip.GetData(_samples, pos))
                    return ComputeRms(_samples);
            }

            _audioSource.GetOutputData(_samples, 0);
            return ComputeRms(_samples);
        }

        private static float ComputeRms(float[] samples)
        {
            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
                sum += samples[i] * samples[i];
            return Mathf.Sqrt(sum / samples.Length);
        }

        // Finds the renderer + blendshape index to drive: the serialized renderer if it has a
        // candidate shape, else the first child renderer that does. Logs what is available when
        // nothing matches so the fix (add the right name to the candidates) is obvious.
        private void ResolveFace()
        {
            if (_face != null && TryFindCandidate(_face, out _blendShapeIndex)) return;

            foreach (SkinnedMeshRenderer renderer in GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (TryFindCandidate(renderer, out _blendShapeIndex))
                {
                    _face = renderer;
                    return;
                }
            }

            Debug.LogWarning($"SimpleLipSync: no candidate blendshape found under '{name}'. " +
                             $"Tried [{string.Join(", ", _blendShapeCandidates)}]. " +
                             $"Available: {DescribeAvailableBlendShapes()}");
        }

        private bool TryFindCandidate(SkinnedMeshRenderer renderer, out int index)
        {
            index = -1;
            Mesh mesh = renderer != null ? renderer.sharedMesh : null;
            if (mesh == null) return false;

            foreach (string candidate in _blendShapeCandidates)
            {
                int found = mesh.GetBlendShapeIndex(candidate);
                if (found >= 0)
                {
                    index = found;
                    Debug.Log($"SimpleLipSync: driving blendshape '{candidate}' on '{renderer.name}'.");
                    return true;
                }
            }
            return false;
        }

        private string DescribeAvailableBlendShapes()
        {
            var sb = new StringBuilder();
            foreach (SkinnedMeshRenderer renderer in GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null || mesh.blendShapeCount == 0) continue;
                sb.Append(renderer.name).Append(": ");
                for (int i = 0; i < mesh.blendShapeCount; i++)
                    sb.Append(mesh.GetBlendShapeName(i)).Append(i < mesh.blendShapeCount - 1 ? ", " : "");
                sb.Append("; ");
            }
            return sb.Length > 0 ? sb.ToString() : "(no blendshapes on any child renderer)";
        }
    }
}
