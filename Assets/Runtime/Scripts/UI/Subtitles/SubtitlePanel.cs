using System.Collections;
using TMPro;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// A reusable world-space subtitle: types a line out character by character on a background that
    /// hugs the text on both axes. Knows nothing about who is speaking — drive it from a presenter
    /// (see <see cref="MerchantSubtitlePresenter"/>) so the same prefab works for any speaker.
    ///
    /// Not to be confused with <see cref="FloatingText"/>, which is the pooled damage/hint popup used
    /// by the crafting systems via <see cref="IFloatingTextService"/>.
    /// </summary>
    public class SubtitlePanel : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The subtitle text. Auto-resolved from children if left empty.")]
        [SerializeField] private TextMeshProUGUI _text;

        [Tooltip("Background panel sized to the text plus Padding. Auto-resolved from the text's " +
                 "parent if left empty.")]
        [SerializeField] private RectTransform _background;

        [Tooltip("The panel's visible content, hidden between lines. Toggled instead of this whole " +
                 "GameObject so anything composed ONTO the panel root (e.g. a nested SummaryCard in the " +
                 "SubtitlePanelWithSummary variant) stays active and keeps its event subscriptions alive. " +
                 "Auto-resolved to the background if left empty.")]
        [SerializeField] private GameObject _contentRoot;

        [Tooltip("Optional accent label for the speaker's name, shown above the line in small caps " +
                 "(e.g. \"AM SAYED\"). Hidden whenever no speaker is supplied; the name is auto-uppercased.")]
        [SerializeField] private TextMeshProUGUI _speakerLabel;

        [Header("Layout")]
        [Tooltip("Space between the text and the edge of the background, per side, in the panel's " +
                 "local units.")]
        [SerializeField] private Vector2 _padding = new Vector2(3f, 10f);

        [Tooltip("A line wraps once it reaches this width; the panel never grows wider than this.")]
        [SerializeField, Min(1f)] private float _maxWidth = 150f;

        [Header("Typing")]
        [Tooltip("Characters revealed per second. 0 shows the whole line at once.")]
        [SerializeField, Min(0f)] private float _charactersPerSecond = 40f;

        [Header("Facing")]
        [Tooltip("Turn to face the camera each frame, staying upright.")]
        [SerializeField] private bool _faceCamera = true;

        [Header("Outline")]
        [Tooltip("Give the caption a crisp dark outline so it stays legible over bright water. Applied to " +
                 "a per-object material instance, so the shared font material (used by the ticket and " +
                 "floating text) is never affected.")]
        [SerializeField] private bool _applyOutline = true;
        [SerializeField] private Color _outlineColor = Color.black;
        [SerializeField, Range(0f, 1f)] private float _outlineWidth = 0.2f;

        private readonly CameraBillboard _billboard = new CameraBillboard();
        private Coroutine _typing;

        /// <summary>True while a line is still being revealed.</summary>
        public bool IsTyping => _typing != null;

        private void Awake()
        {
            if (_text == null) _text = GetComponentInChildren<TextMeshProUGUI>(true);
            if (_background == null && _text != null)
                _background = _text.rectTransform.parent as RectTransform;
            if (_contentRoot == null && _background != null) _contentRoot = _background.gameObject;

            if (_text == null)
                Debug.LogWarning($"{nameof(SubtitlePanel)} on '{name}' has no TextMeshProUGUI — " +
                                 "it cannot show subtitles.", this);

            if (_applyOutline && _text != null)
            {
                // fontMaterial returns an instance unique to this text object, so the shared font
                // material is left untouched. Outline is a core SDF property — no shader keyword needed.
                Material mat = _text.fontMaterial;
                mat.SetFloat(ShaderUtilities.ID_OutlineWidth, _outlineWidth);
                mat.SetColor(ShaderUtilities.ID_OutlineColor, _outlineColor);
                _text.UpdateMeshPadding();
            }

            if (_speakerLabel != null) _speakerLabel.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            // Face the player on the very first frame rather than one frame late, so the caption is
            // never glimpsed at its authored rotation.
            FaceCamera();
        }

        private void LateUpdate()
        {
            FaceCamera();
        }

        private void FaceCamera()
        {
            if (_faceCamera) _billboard.Apply(transform);
        }

        /// <summary>Shows a line, revealing it at <see cref="_charactersPerSecond"/>.</summary>
        public void Show(string text) => Show(text, null);

        /// <summary>Shows a line under a named speaker, revealing it at <see cref="_charactersPerSecond"/>.</summary>
        public void Show(string text, string speaker)
        {
            SetSpeaker(speaker);
            Display(text, instant: _charactersPerSecond <= 0f);
        }

        /// <summary>Shows a line in full immediately, with no typing.</summary>
        public void ShowInstant(string text) => ShowInstant(text, null);

        /// <summary>Shows a line under a named speaker in full immediately, with no typing.</summary>
        public void ShowInstant(string text, string speaker)
        {
            SetSpeaker(speaker);
            Display(text, instant: true);
        }

        // Fills the accent label with the speaker's name (upper-cased) or hides it when none is given.
        private void SetSpeaker(string speaker)
        {
            if (_speakerLabel == null) return;
            bool has = !string.IsNullOrWhiteSpace(speaker);
            _speakerLabel.gameObject.SetActive(has);
            if (!has) return;

            // Shaped as well: a speaker named in Arabic needs the same treatment as the line itself.
            // ToUpperInvariant is a no-op on Arabic, so it stays safe for both scripts.
            string name = speaker.ToUpperInvariant();
            _speakerLabel.isRightToLeftText = ArabicTextShaper.ContainsArabic(name);
            _speakerLabel.text = ArabicTextShaper.Shape(name);
        }

        /// <summary>Reveals the rest of the current line at once.</summary>
        public void CompleteTyping()
        {
            if (_typing == null) return;
            StopCoroutine(_typing);
            _typing = null;
            _text.maxVisibleCharacters = int.MaxValue;
        }

        public void Hide()
        {
            StopTyping();
            SetContentActive(false);
        }

        // Shows/hides the caption's visuals WITHOUT touching this GameObject, so the panel root — and
        // anything nested under it — stays active and keeps running (see _contentRoot). Falls back to the
        // whole GameObject only if no content root could be resolved.
        private void SetContentActive(bool active)
        {
            if (_contentRoot != null) _contentRoot.SetActive(active);
            else gameObject.SetActive(active);
        }

        private void Display(string text, bool instant)
        {
            if (_text == null) return;

            StopTyping();

            if (string.IsNullOrWhiteSpace(text))
            {
                Hide();
                return;
            }

            SetContentActive(true);
            Resize(text);

            if (instant || !isActiveAndEnabled)
            {
                _text.maxVisibleCharacters = int.MaxValue;
                return;
            }

            _typing = StartCoroutine(TypeOut());
        }

        // Sizes the panel to the FULL line before a single character is revealed. This is what lets the
        // background hug the text on both axes AND type at the same time: wrapping is solved once, up
        // front, so revealing characters never reflows the layout and the panel never jitters.
        private void Resize(string text)
        {
            // TMP cannot shape Arabic, so the line is rewritten into connected letterforms and the
            // component is flipped to right-to-left. Both are needed: shaping alone leaves the words in
            // English reading order, RTL alone leaves the letters disconnected. Measuring uses the
            // SHAPED string — lam+alef collapses two characters into one glyph, so measuring the raw
            // text would size the panel for a line that is never drawn.
            string shaped = ArabicTextShaper.Shape(text);
            _text.isRightToLeftText = ArabicTextShaper.ContainsArabic(text);

            _text.text = shaped;
            _text.maxVisibleCharacters = 0;

            Vector2 preferred = _text.GetPreferredValues(shaped, _maxWidth, 0f);
            preferred.x = Mathf.Min(preferred.x, _maxWidth);

            _text.rectTransform.sizeDelta = preferred;
            if (_background != null) _background.sizeDelta = preferred + _padding * 2f;
        }

        private IEnumerator TypeOut()
        {
            // The character count is only valid once TMP has laid the text out.
            _text.ForceMeshUpdate();
            int total = _text.textInfo.characterCount;

            float revealed = 0f;
            while (revealed < total)
            {
                revealed += _charactersPerSecond * Time.deltaTime;
                _text.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(revealed));
                yield return null;
            }

            _text.maxVisibleCharacters = int.MaxValue;
            _typing = null;
        }

        private void StopTyping()
        {
            if (_typing == null) return;
            StopCoroutine(_typing);
            _typing = null;
        }
    }
}
