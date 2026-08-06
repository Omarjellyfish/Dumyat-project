using System.Collections;
using TMPro;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Sends the shock of a blow through the word one character at a time, starting at the letter
    /// nearest the nail — so the word does not shudder as a block, it visibly conducts the impact.
    ///
    /// Owns the label's MESH VERTICES. It is the one effect that has to fight TextMeshPro for the
    /// geometry, so it re-reads the typeset mesh every frame instead of caching it (see below).
    /// </summary>
    public class NailLabelJolt : NailStrikeBehaviour
    {
        [Header("Jolt")]
        [Tooltip("Text to displace. Taken from this GameObject when left empty.")]
        [SerializeField] private TMP_Text _label;

        [Tooltip("Run the shock from the last character to the first. Turn this on for a label that " +
                 "sits to the LEFT of its nail, so the wave still starts at the end nearest the blow.")]
        [SerializeField] private bool _fromEnd;

        private Coroutine _running;

        protected override void Awake()
        {
            base.Awake();

            if (_label == null) _label = GetComponent<TMP_Text>();
            if (_label == null)
                Debug.LogWarning($"{nameof(NailLabelJolt)} on '{name}' has no {nameof(TMP_Text)} — " +
                                 "the letters will not react to the hammer.", this);
        }

        protected override void HandleStruck(NailStrike strike)
        {
            if (_label == null || Style == null || !isActiveAndEnabled) return;

            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(Jolt());
        }

        protected override void HandleRestored()
        {
            if (_running != null)
            {
                StopCoroutine(_running);
                _running = null;
            }

            if (_label != null) _label.ForceMeshUpdate();
        }

        /// <summary>
        /// Re-reads the typeset geometry every frame rather than snapshotting it once.
        ///
        /// That looks wasteful and is deliberate: <see cref="NailLabelDrive"/> is changing the
        /// label's colour and mesh padding at the same time, and each of those makes TextMeshPro
        /// rebuild the mesh. A cached snapshot taken before the rebuild would be applied against
        /// geometry that no longer matches it, and the word would tear. Rebuilding first, then
        /// displacing, means the two effects compose instead of colliding.
        /// </summary>
        private IEnumerator Jolt()
        {
            float duration = Style.JoltSeconds;
            float delay = Style.JoltPerCharacterDelay;
            float amplitude = Style.JoltAmplitude;
            float spin = Style.JoltRotationDegrees;
            AnimationCurve curve = Style.JoltCurve;

            _label.ForceMeshUpdate();
            int characterCount = _label.textInfo.characterCount;
            if (characterCount == 0)
            {
                _running = null;
                yield break;
            }

            float total = duration + delay * characterCount;

            for (float elapsed = 0f; elapsed < total; elapsed += Time.unscaledDeltaTime)
            {
                _label.ForceMeshUpdate();
                TMP_TextInfo info = _label.textInfo;

                // The text could have been re-laid out under us (a language switch mid-blow).
                // Re-reading the count each frame keeps the indices honest.
                int count = info.characterCount;

                for (int i = 0; i < count; i++)
                {
                    TMP_CharacterInfo character = info.characterInfo[i];
                    if (!character.isVisible) continue;

                    int order = _fromEnd ? count - 1 - i : i;
                    float local = (elapsed - order * delay) / duration;
                    if (local < 0f || local > 1f) continue;   // not started, or already settled

                    float amount = curve != null ? curve.Evaluate(local) : 0f;

                    Vector3[] vertices = info.meshInfo[character.materialReferenceIndex].vertices;
                    int index = character.vertexIndex;

                    // Rotate about the character's own centre so it twists in place rather than
                    // swinging away from the baseline.
                    Vector3 centre = (vertices[index] + vertices[index + 2]) * 0.5f;
                    Quaternion twist = Quaternion.Euler(0f, 0f, -spin * amount);
                    Vector3 shift = new Vector3(0f, -amplitude * amount, 0f);

                    for (int v = 0; v < 4; v++)
                        vertices[index + v] = twist * (vertices[index + v] - centre) + centre + shift;
                }

                for (int m = 0; m < info.meshInfo.Length; m++)
                {
                    info.meshInfo[m].mesh.vertices = info.meshInfo[m].vertices;
                    _label.UpdateGeometry(info.meshInfo[m].mesh, m);
                }

                yield return null;
            }

            _label.ForceMeshUpdate();   // back to the typeset positions
            _running = null;
        }
    }
}
