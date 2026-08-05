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
        [SerializeField, Min(0.1f)] private float _gain = 9f;

        [Tooltip("Maximum blendshape weight (0-100) at full loudness — how WIDE the mouth opens at its " +
                 "loudest. Higher = more open. (100 is fully open.)")]
        [SerializeField, Range(0f, 100f)] private float _maxWeight = 95f;

        [Tooltip("How fast the mouth opens toward the target (per second, in 0-1 openness units).")]
        [SerializeField, Min(0.1f)] private float _attackSpeed = 14f;

        [Tooltip("How fast the mouth closes when the audio quietens or stops.")]
        [SerializeField, Min(0.1f)] private float _releaseSpeed = 8f;

        [Header("Eye Blink")]
        [Tooltip("Enable random, natural eye blinking to keep the face alive while it speaks.")]
        [SerializeField] private bool _enableBlink = true;

        [Tooltip("Left-eye blink blendshape (ARKit / Ready Player Me). PREFERRED: when this and the " +
                 "right one below both exist, they are driven (isolated eyelids). ")]
        [SerializeField] private string _leftBlinkBlendShape = "eyeBlinkLeft";

        [Tooltip("Right-eye blink blendshape (ARKit / Ready Player Me). Paired with the left one above.")]
        [SerializeField] private string _rightBlinkBlendShape = "eyeBlinkRight";

        [Tooltip("FALLBACK single blendshape that closes both eyes, tried in order — only used when the " +
                 "paired left/right shapes above are NOT both present. NOTE: on Wolf3D / Ready Player Me, " +
                 "'eyesClosed' is a fuller expression that drags the whole eye region, so it is intentionally " +
                 "a fallback, not the default.")]
        [SerializeField] private string[] _blinkBlendShapeCandidates = { "eyesClosed", "eyeBlink", "blink" };

        [Tooltip("Shortest / longest gap between blinks, in seconds. The next gap is random in this range.")]
        [SerializeField, Min(0.05f)] private float _minBlinkInterval = 2.5f;
        [SerializeField, Min(0.05f)] private float _maxBlinkInterval = 6f;

        [Tooltip("How long the eyelids take to close, then to reopen (seconds). A real blink is quick.")]
        [SerializeField, Min(0.02f)] private float _blinkCloseTime = 0.06f;
        [SerializeField, Min(0.02f)] private float _blinkOpenTime = 0.12f;

        [Tooltip("Blendshape weight (0-100) at a fully closed eye.")]
        [SerializeField, Range(0f, 100f)] private float _blinkClosedWeight = 100f;

        [Tooltip("Blink ONLY while the character is speaking (audio playing). Off = blink continuously " +
                 "(more natural — a person blinks whether or not they are talking).")]
        [SerializeField] private bool _blinkOnlyWhileSpeaking = false;

        private readonly float[] _samples = new float[256];
        private int _blendShapeIndex = -1;
        private float _openness; // current 0..1 smoothed openness

        // --- Blink state ---
        private enum BlinkPhase { Waiting, Closing, Opening }
        private BlinkPhase _blinkPhase = BlinkPhase.Waiting;
        private bool _hasBlink;
        private float _blinkTimer;
        private float _nextBlinkInterval;
        private float _blinkAmount; // 0..1 closed
        private SkinnedMeshRenderer _blinkCombinedRenderer;
        private int _blinkCombinedIndex = -1;
        private SkinnedMeshRenderer _blinkLeftRenderer;
        private int _blinkLeftIndex = -1;
        private SkinnedMeshRenderer _blinkRightRenderer;
        private int _blinkRightIndex = -1;

        private void Start()
        {
            if (_audioSource == null) _audioSource = GetComponent<AudioSource>();

            ResolveFace();
            ResolveBlink();
            ScheduleNextBlink();

            bool canLipSync = _audioSource != null && _blendShapeIndex >= 0;
            if (!canLipSync)
                Debug.LogWarning($"SimpleLipSync: lip movement disabled on '{name}' " +
                                 "(no AudioSource or no mouth blendshape found).");

            // Keep the component alive if EITHER mouth or blink has something to drive; disable only
            // when there is nothing at all to animate.
            if (!canLipSync && !_hasBlink) enabled = false;
        }

        // LateUpdate so the weights land after any Animator evaluation this frame.
        private void LateUpdate()
        {
            UpdateMouth();
            UpdateBlink();
        }

        // Amplitude-driven mouth opening (no-op when there's no audio/mouth blendshape).
        private void UpdateMouth()
        {
            if (_blendShapeIndex < 0 || _audioSource == null) return;

            float target = 0f;
            if (_audioSource.isPlaying)
                target = Mathf.Clamp01(ReadSourceRms() * _gain);

            float speed = target > _openness ? _attackSpeed : _releaseSpeed;
            _openness = Mathf.MoveTowards(_openness, target, speed * Time.deltaTime);
            _face.SetBlendShapeWeight(_blendShapeIndex, _openness * _maxWeight);
        }

        // --- Eye blink -------------------------------------------------------------

        // Random natural blinking: wait a random gap, snap the lids closed, then ease them open, repeat.
        private void UpdateBlink()
        {
            if (!_hasBlink) return;

            float dt = Time.deltaTime;
            switch (_blinkPhase)
            {
                case BlinkPhase.Waiting:
                    // Optionally only blink while speaking; otherwise blink continuously (more natural).
                    if (_blinkOnlyWhileSpeaking && (_audioSource == null || !_audioSource.isPlaying))
                        return;
                    _blinkTimer += dt;
                    if (_blinkTimer >= _nextBlinkInterval)
                    {
                        _blinkPhase = BlinkPhase.Closing;
                        _blinkTimer = 0f;
                    }
                    break;

                case BlinkPhase.Closing:
                    _blinkTimer += dt;
                    _blinkAmount = _blinkCloseTime > 0f ? Mathf.Clamp01(_blinkTimer / _blinkCloseTime) : 1f;
                    if (_blinkTimer >= _blinkCloseTime)
                    {
                        _blinkAmount = 1f;
                        _blinkPhase = BlinkPhase.Opening;
                        _blinkTimer = 0f;
                    }
                    break;

                case BlinkPhase.Opening:
                    _blinkTimer += dt;
                    _blinkAmount = _blinkOpenTime > 0f ? 1f - Mathf.Clamp01(_blinkTimer / _blinkOpenTime) : 0f;
                    if (_blinkTimer >= _blinkOpenTime)
                    {
                        _blinkAmount = 0f;
                        ScheduleNextBlink();
                    }
                    break;
            }

            ApplyBlink(_blinkAmount);
        }

        private void ScheduleNextBlink()
        {
            _blinkPhase = BlinkPhase.Waiting;
            _blinkTimer = 0f;
            _nextBlinkInterval = Random.Range(_minBlinkInterval, Mathf.Max(_minBlinkInterval, _maxBlinkInterval));
        }

        // Writes the closed amount (0..1) to whichever blink blendshape(s) were resolved.
        private void ApplyBlink(float amount)
        {
            float w = amount * _blinkClosedWeight;
            if (_blinkCombinedIndex >= 0) _blinkCombinedRenderer.SetBlendShapeWeight(_blinkCombinedIndex, w);
            if (_blinkLeftIndex >= 0) _blinkLeftRenderer.SetBlendShapeWeight(_blinkLeftIndex, w);
            if (_blinkRightIndex >= 0) _blinkRightRenderer.SetBlendShapeWeight(_blinkRightIndex, w);
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

        // Resolves the eye-blink blendshape(s): a single combined "close both eyes" shape if the rig has
        // one, otherwise the paired left/right shapes (ARKit / Ready Player Me). Searches the resolved
        // face renderer first, then any child renderer (eyes are sometimes a separate mesh).
        private void ResolveBlink()
        {
            if (!_enableBlink) return;

            _blinkCombinedIndex = _blinkLeftIndex = _blinkRightIndex = -1;

            // Look up the paired ARKit eyelid shapes up front (used as preferred or as a single-side
            // last resort). Held in locals so a partial match doesn't leave a stray index that ApplyBlink
            // would drive alongside the combined shape.
            bool hasLeft = FindShape(_leftBlinkBlendShape, out SkinnedMeshRenderer leftR, out int leftI);
            bool hasRight = FindShape(_rightBlinkBlendShape, out SkinnedMeshRenderer rightR, out int rightI);

            // PREFERRED: both isolated eyelid shapes — moves only the lids, no brow/cheek drag. This is
            // the correct path for ARKit / Ready Player Me / Wolf3D rigs (which also ship a broader
            // 'eyesClosed' morph that would distort the whole eye region).
            if (hasLeft && hasRight)
            {
                _blinkLeftRenderer = leftR; _blinkLeftIndex = leftI;
                _blinkRightRenderer = rightR; _blinkRightIndex = rightI;
                _hasBlink = true;
                Debug.Log($"SimpleLipSync: blinking via '{_leftBlinkBlendShape}'/'{_rightBlinkBlendShape}'.");
                return;
            }

            // FALLBACK: a single combined "close both eyes" shape, for rigs without the ARKit pair.
            foreach (string candidate in _blinkBlendShapeCandidates)
            {
                if (FindShape(candidate, out _blinkCombinedRenderer, out _blinkCombinedIndex))
                {
                    _hasBlink = true;
                    Debug.Log($"SimpleLipSync: blinking via combined '{candidate}' on '{_blinkCombinedRenderer.name}'.");
                    return;
                }
            }

            // LAST RESORT: only one side of the pair exists.
            if (hasLeft || hasRight)
            {
                if (hasLeft) { _blinkLeftRenderer = leftR; _blinkLeftIndex = leftI; }
                if (hasRight) { _blinkRightRenderer = rightR; _blinkRightIndex = rightI; }
                _hasBlink = true;
                Debug.Log("SimpleLipSync: blinking via a single eyelid shape.");
                return;
            }

            Debug.LogWarning($"SimpleLipSync: no eye-blink blendshape found under '{name}' " +
                             $"(tried '{_leftBlinkBlendShape}'/'{_rightBlinkBlendShape}' and " +
                             $"[{string.Join(", ", _blinkBlendShapeCandidates)}]). Blinking disabled. " +
                             $"Available: {DescribeAvailableBlendShapes()}");
        }

        // Finds a named blendshape on the face renderer first, then any child renderer.
        private bool FindShape(string shapeName, out SkinnedMeshRenderer renderer, out int index)
        {
            renderer = null;
            index = -1;
            if (string.IsNullOrEmpty(shapeName)) return false;

            if (_face != null)
            {
                int i = IndexOf(_face, shapeName);
                if (i >= 0) { renderer = _face; index = i; return true; }
            }

            foreach (SkinnedMeshRenderer r in GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                int i = IndexOf(r, shapeName);
                if (i >= 0) { renderer = r; index = i; return true; }
            }
            return false;
        }

        private static int IndexOf(SkinnedMeshRenderer renderer, string shapeName)
        {
            Mesh mesh = renderer != null ? renderer.sharedMesh : null;
            return mesh != null ? mesh.GetBlendShapeIndex(shapeName) : -1;
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
