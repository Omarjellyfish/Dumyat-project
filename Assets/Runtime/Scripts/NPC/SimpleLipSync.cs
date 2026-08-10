using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Amplitude-driven mouth movement ("lip flap") for a speaking character — deliberately NOT
    /// phoneme-accurate. While the character's <see cref="AudioSource"/> is playing (e.g. the
    /// merchant's TTS line), the mouth blendshape opens in proportion to the loudness of the
    /// audio each frame, which naturally flutters with speech; when playback stops the mouth
    /// eases shut. Self-contained: no packages, no events — it only watches the AudioSource.
    ///
    /// Rig-agnostic by name lists rather than by rig type. The defaults cover the three families
    /// used in this project: Ready Player Me / Wolf3D (viseme_aa, eyeBlinkLeft/Right), ARKit
    /// (jawOpen), and Reallusion Character Creator (V_Open, Eye_Blink_L/R). Swapping a character
    /// for one built in a different tool should need no code change — add its names here.
    ///
    /// IMPORTANT: every renderer that carries the chosen blendshape is driven, not just the first
    /// one found. A CC export splits the face across several meshes (body, brows, eyelashes, beard)
    /// that each carry their own copy of the same shape; driving only one deforms the mouth while
    /// the beard and brows stay rigid. Rigs with a single face mesh are unaffected.
    /// </summary>
    public class SimpleLipSync : MonoBehaviour
    {
        /// <summary>A pair of isolated eyelid blendshapes, tried together (both must exist).</summary>
        [Serializable]
        public struct EyelidPair
        {
            public string Left;
            public string Right;
        }

        [Tooltip("Audio source the character speaks through (the merchant TTS source). " +
                 "Auto-resolved from this GameObject when empty.")]
        [SerializeField] private AudioSource _audioSource;

        [Tooltip("Renderer searched FIRST for the blendshapes below. Optional — leave empty to search " +
                 "all child renderers. Note this only sets the search order: every renderer carrying " +
                 "the chosen shape is driven, so a split face stays in sync.")]
        [SerializeField] private SkinnedMeshRenderer _face;

        [Tooltip("EXTRA mouth blendshape names for a rig the built-in list below does not cover. Tried " +
                 "BEFORE the built-ins, so naming one here overrides them. Normally left empty — if you " +
                 "find yourself filling it in for a whole character family, add the names to " +
                 "BuiltInMouthShapes in code instead so every existing character picks them up.")]
        [SerializeField] private string[] _extraMouthShapes = new string[0];

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

        [Tooltip("EXTRA isolated eyelid pairs for a rig the built-in list does not cover. Tried BEFORE " +
                 "the built-ins. Both sides of a pair must exist for it to be used.")]
        [SerializeField] private EyelidPair[] _extraEyelidPairs = new EyelidPair[0];

        [Tooltip("EXTRA single 'close both eyes' shapes, used only when no eyelid pair matches. Tried " +
                 "BEFORE the built-ins.")]
        [SerializeField] private string[] _extraCombinedBlinkShapes = new string[0];

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

        // ---------------------------------------------------------------------------------------
        // Known rig conventions. These live in CODE, not in a [SerializeField] default.
        //
        // A serialized array's initializer only applies to a NEWLY added component: every character
        // already placed in a scene or prefab keeps whatever was written to its YAML the first time,
        // so extending the list in code would not reach a single existing NPC. That is exactly how the
        // Character Creator merchants ended up still reporting "Tried [viseme_aa, jawOpen, mouthOpen]"
        // after those names had been added — the fix compiled, and nothing used it. Supporting a new
        // rig family must be a one-line code change that every existing character picks up on reload,
        // so the built-in names are static and the serialized fields above are additive overrides only.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Mouth-open shapes by rig family, STRONGEST FIRST — the order is the tie-break when the shapes
        /// cannot be measured (see <see cref="MeasureAmplitude"/>), so a barely-moving shape must not sit
        /// near the front. Reallusion's 'V_Open' is last for exactly that reason: it is present on every
        /// CC head but moves vertices by ~2 mm, an order of magnitude less than Merged_Open_Mouth, and
        /// looks like a closed mouth however far you drive it.
        /// </summary>
        private static readonly string[] BuiltInMouthShapes =
        {
            "viseme_aa", "jawOpen", "mouthOpen",               // Ready Player Me / Wolf3D / ARKit
            "Merged_Open_Mouth", "Jaw_Open", "A25_Jaw_Open",   // Reallusion Character Creator (CC3 / CC4)
            "V_Open",                                          // CC viseme — present everywhere, barely moves
        };

        /// <summary>
        /// Below this much vertex movement (metres, at full weight) a shape cannot visibly open a mouth.
        /// Set between CC's dud 'V_Open' (~0.002) and its real 'Merged_Open_Mouth' (~0.025).
        /// </summary>
        private const float MinUsefulAmplitude = 0.006f;

        /// <summary>
        /// Isolated eyelid pairs, preferred over the combined shapes below because they move only the
        /// lids — no brow or cheek drag.
        /// </summary>
        private static readonly EyelidPair[] BuiltInEyelidPairs =
        {
            new EyelidPair { Left = "eyeBlinkLeft",       Right = "eyeBlinkRight" },        // Ready Player Me / ARKit
            new EyelidPair { Left = "Eye_Blink_L",        Right = "Eye_Blink_R" },          // Reallusion CC3 / CC4
            new EyelidPair { Left = "A14_Eye_Blink_Left", Right = "A15_Eye_Blink_Right" },  // CC4 ARKit naming
        };

        /// <summary>
        /// Single "close both eyes" shapes. A fallback, not a default: on Wolf3D / Ready Player Me
        /// 'eyesClosed' is a fuller expression that drags the whole eye region.
        /// </summary>
        private static readonly string[] BuiltInCombinedBlinkShapes =
        {
            "eyesClosed", "eyeBlink", "blink",   // Ready Player Me / Wolf3D
            "Eyes_Blink",                        // Reallusion Character Creator
        };

        /// <summary>One blendshape on one renderer. A face split across meshes yields several.</summary>
        private readonly struct ShapeTarget
        {
            public readonly SkinnedMeshRenderer Renderer;
            public readonly int Index;

            public ShapeTarget(SkinnedMeshRenderer renderer, int index)
            {
                Renderer = renderer;
                Index = index;
            }
        }

        private readonly float[] _samples = new float[256];
        private readonly List<ShapeTarget> _mouthTargets = new List<ShapeTarget>();
        private readonly List<ShapeTarget> _blinkTargets = new List<ShapeTarget>();
        private float _openness; // current 0..1 smoothed openness

        // --- Blink state ---
        private enum BlinkPhase { Waiting, Closing, Opening }
        private BlinkPhase _blinkPhase = BlinkPhase.Waiting;
        private float _blinkTimer;
        private float _nextBlinkInterval;
        private float _blinkAmount; // 0..1 closed

        private bool HasMouth => _mouthTargets.Count > 0;
        private bool HasBlink => _blinkTargets.Count > 0;

        private void Start()
        {
            if (_audioSource == null) _audioSource = GetComponent<AudioSource>();

            ResolveFace();
            ResolveBlink();
            ScheduleNextBlink();

            bool canLipSync = _audioSource != null && HasMouth;
            if (!canLipSync)
                Debug.LogWarning($"SimpleLipSync: lip movement disabled on '{name}' " +
                                 "(no AudioSource or no mouth blendshape found).", this);

            // Keep the component alive if EITHER mouth or blink has something to drive; disable only
            // when there is nothing at all to animate.
            if (!canLipSync && !HasBlink) enabled = false;
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
            if (!HasMouth || _audioSource == null) return;

            float target = 0f;
            if (_audioSource.isPlaying)
                target = Mathf.Clamp01(ReadSourceRms() * _gain);

            float speed = target > _openness ? _attackSpeed : _releaseSpeed;
            _openness = Mathf.MoveTowards(_openness, target, speed * Time.deltaTime);
            Write(_mouthTargets, _openness * _maxWeight);
        }

        // --- Eye blink -------------------------------------------------------------

        // Random natural blinking: wait a random gap, snap the lids closed, then ease them open, repeat.
        private void UpdateBlink()
        {
            if (!HasBlink) return;

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

            Write(_blinkTargets, _blinkAmount * _blinkClosedWeight);
        }

        private void ScheduleNextBlink()
        {
            _blinkPhase = BlinkPhase.Waiting;
            _blinkTimer = 0f;
            _nextBlinkInterval = UnityEngine.Random.Range(_minBlinkInterval, Mathf.Max(_minBlinkInterval, _maxBlinkInterval));
        }

        private static void Write(List<ShapeTarget> targets, float weight)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                ShapeTarget t = targets[i];
                if (t.Renderer != null) t.Renderer.SetBlendShapeWeight(t.Index, weight);
            }
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

        // Picks the first candidate name present anywhere under this character, then drives it on
        // EVERY renderer that has it. Logs what is available when nothing matches so the fix (add the
        // right name to the candidates) is obvious.
        private void ResolveFace()
        {
            _mouthTargets.Clear();

            // Every candidate this character actually has, in list order — and the densest mesh among
            // those carrying them, which is the FACE (see the note on scoring below).
            var present = new List<string>();
            var probe = new List<ShapeTarget>();
            SkinnedMeshRenderer face = null;

            foreach (string candidate in Ordered(_extraMouthShapes, BuiltInMouthShapes))
            {
                probe.Clear();
                CollectShape(candidate, probe);
                if (probe.Count == 0) continue;
                present.Add(candidate);

                for (int i = 0; i < probe.Count; i++)
                {
                    SkinnedMeshRenderer r = probe[i].Renderer;
                    if (r == null || r.sharedMesh == null) continue;
                    if (face == null || r.sharedMesh.vertexCount > face.sharedMesh.vertexCount) face = r;
                }
            }

            if (present.Count == 0)
            {
                Debug.LogWarning($"SimpleLipSync: no candidate mouth blendshape found under '{name}'. " +
                                 $"Tried [{string.Join(", ", Ordered(_extraMouthShapes, BuiltInMouthShapes))}]. " +
                                 $"Available: {DescribeAvailableBlendShapes()}", this);
                return;
            }

            // Choose by how far the shape actually MOVES THE FACE, not by name alone and not by its
            // effect on any old mesh. Two traps, both hit for real on these characters:
            //
            //  * A name match is not proof of a working shape. CC heads ship a full viseme set in which
            //    several entries are near-empty: 'V_Open' applied at weight 70 across five renderers
            //    still left the mouth visibly shut, because it moves the face all of 2 mm.
            //  * Scoring a shape by its strongest effect on ANY mesh picks the wrong one. Hagg Mahrous
            //    carries a separate beard mesh that 'A25_Jaw_Open' swings by 57 mm while moving his
            //    actual face only 10 mm — so "strongest anywhere" chose it over 'Merged_Open_Mouth',
            //    which moves the face 46 mm, and his mouth stayed shut.
            //
            // So every candidate is scored on ONE mesh: the densest one carrying any of them, which is
            // reliably the face rather than a beard, brow or eyelash card. List order is the fallback,
            // for when that mesh is not readable at run time.
            string chosen = present[0];
            float chosenAmplitude = AmplitudeOn(face, chosen);

            if (chosenAmplitude >= 0f)
            {
                foreach (string candidate in present)
                {
                    float amplitude = AmplitudeOn(face, candidate);
                    if (amplitude <= chosenAmplitude) continue;
                    chosen = candidate;
                    chosenAmplitude = amplitude;
                }
            }

            CollectShape(chosen, _mouthTargets);

            if (chosenAmplitude >= 0f && chosenAmplitude < MinUsefulAmplitude)
                Debug.LogWarning($"SimpleLipSync: the best mouth shape on '{name}' is '{chosen}', but it " +
                                 $"only moves the mesh {chosenAmplitude:0.####} m — too little to read as " +
                                 "an open mouth. This character's mouth will barely move. Add a stronger " +
                                 "shape's name to Extra Mouth Shapes. " +
                                 $"Available: {DescribeAvailableBlendShapes()}", this);
            else
                VerboseLog.Trace($"SimpleLipSync: driving mouth blendshape '{chosen}' on " +
                          $"{_mouthTargets.Count} renderer(s) under '{name}'" +
                          (chosenAmplitude >= 0f
                              ? $" (amplitude {chosenAmplitude:0.####} m)."
                              : " (chosen by list order — a face mesh here has Read/Write Enabled off, " +
                                "so how far each shape moves could not be measured)."), this);
        }

        /// <summary>
        /// How far <paramref name="shapeName"/> moves <paramref name="face"/> at full weight, in metres.
        /// Returns 0 when the face does not carry the shape at all (it cannot open a face it is not on),
        /// and -1 when the face cannot be measured — Read/Write Enabled is off on that model's import, so
        /// the caller must fall back to list order rather than guess. Measured once, at startup.
        /// </summary>
        private static float AmplitudeOn(SkinnedMeshRenderer face, string shapeName)
        {
            Mesh mesh = face != null ? face.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) return -1f;

            int index = mesh.GetBlendShapeIndex(shapeName);
            if (index < 0) return 0f;

            try
            {
                var deltas = new Vector3[mesh.vertexCount];
                int lastFrame = mesh.GetBlendShapeFrameCount(index) - 1;
                mesh.GetBlendShapeFrameVertices(index, lastFrame, deltas, null, null);

                float maxSqr = 0f;
                for (int i = 0; i < deltas.Length; i++)
                {
                    float sqr = deltas[i].sqrMagnitude;
                    if (sqr > maxSqr) maxSqr = sqr;
                }
                return Mathf.Sqrt(maxSqr);
            }
            catch (Exception)
            {
                // Mesh introspection varies by import settings and platform; an unmeasurable shape is
                // not a failure, it just means list order decides.
                return -1f;
            }
        }

        // Resolves the eye-blink blendshape(s): the first isolated left/right pair whose BOTH sides
        // exist, else a single combined "close both eyes" shape, else a lone eyelid as a last resort.
        private void ResolveBlink()
        {
            _blinkTargets.Clear();
            if (!_enableBlink) return;

            // PREFERRED: an isolated eyelid pair — moves only the lids, no brow/cheek drag.
            var left = new List<ShapeTarget>();
            var right = new List<ShapeTarget>();
            foreach (EyelidPair pair in Ordered(_extraEyelidPairs, BuiltInEyelidPairs))
            {
                left.Clear();
                right.Clear();
                CollectShape(pair.Left, left);
                CollectShape(pair.Right, right);
                if (left.Count == 0 || right.Count == 0) continue;

                _blinkTargets.AddRange(left);
                _blinkTargets.AddRange(right);
                VerboseLog.Trace($"SimpleLipSync: blinking via '{pair.Left}'/'{pair.Right}' on " +
                          $"{_blinkTargets.Count} renderer(s) under '{name}'.", this);
                return;
            }

            // FALLBACK: a single combined "close both eyes" shape, for rigs without an eyelid pair.
            foreach (string candidate in Ordered(_extraCombinedBlinkShapes, BuiltInCombinedBlinkShapes))
            {
                CollectShape(candidate, _blinkTargets);
                if (_blinkTargets.Count == 0) continue;

                VerboseLog.Trace($"SimpleLipSync: blinking via combined '{candidate}' under '{name}'.", this);
                return;
            }

            // LAST RESORT: only one side of a pair exists anywhere.
            foreach (EyelidPair pair in Ordered(_extraEyelidPairs, BuiltInEyelidPairs))
            {
                CollectShape(pair.Left, _blinkTargets);
                CollectShape(pair.Right, _blinkTargets);
                if (_blinkTargets.Count == 0) continue;

                VerboseLog.Trace($"SimpleLipSync: blinking via a single eyelid shape under '{name}'.", this);
                return;
            }

            Debug.LogWarning($"SimpleLipSync: no eye-blink blendshape found under '{name}' " +
                             $"(tried {DescribePairs()} and " +
                             $"[{string.Join(", ", Ordered(_extraCombinedBlinkShapes, BuiltInCombinedBlinkShapes))}]). " +
                             $"Blinking disabled. Available: {DescribeAvailableBlendShapes()}", this);
        }

        // Serialized overrides first, then the built-in rig conventions. Nulls are tolerated because a
        // component deserialized from older YAML can arrive with the array field unset.
        private static IEnumerable<T> Ordered<T>(T[] overrides, T[] builtIn)
        {
            if (overrides != null)
            {
                foreach (T item in overrides) yield return item;
            }
            foreach (T item in builtIn) yield return item;
        }

        // Appends a target for every child renderer carrying <paramref name="shapeName"/>. The
        // serialized _face renderer is searched first purely so it wins the ordering when several
        // meshes could satisfy an earlier candidate name.
        private void CollectShape(string shapeName, List<ShapeTarget> into)
        {
            if (string.IsNullOrEmpty(shapeName)) return;

            if (_face != null)
            {
                int i = IndexOf(_face, shapeName);
                if (i >= 0) into.Add(new ShapeTarget(_face, i));
            }

            foreach (SkinnedMeshRenderer r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r == _face) continue;   // already added above
                int i = IndexOf(r, shapeName);
                if (i >= 0) into.Add(new ShapeTarget(r, i));
            }
        }

        private static int IndexOf(SkinnedMeshRenderer renderer, string shapeName)
        {
            Mesh mesh = renderer != null ? renderer.sharedMesh : null;
            return mesh != null ? mesh.GetBlendShapeIndex(shapeName) : -1;
        }

        private string DescribePairs()
        {
            var sb = new StringBuilder();
            foreach (EyelidPair p in Ordered(_extraEyelidPairs, BuiltInEyelidPairs))
                sb.Append('\'').Append(p.Left).Append("'/'").Append(p.Right).Append("' ");
            return sb.ToString().TrimEnd();
        }

        private string DescribeAvailableBlendShapes()
        {
            var sb = new StringBuilder();
            foreach (SkinnedMeshRenderer renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
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
