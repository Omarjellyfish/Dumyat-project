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

        private Camera _camera;
        private Coroutine _typing;

        /// <summary>True while a line is still being revealed.</summary>
        public bool IsTyping => _typing != null;

        private void Awake()
        {
            if (_text == null) _text = GetComponentInChildren<TextMeshProUGUI>(true);
            if (_background == null && _text != null)
                _background = _text.rectTransform.parent as RectTransform;

            if (_text == null)
                Debug.LogWarning($"{nameof(SubtitlePanel)} on '{name}' has no TextMeshProUGUI — " +
                                 "it cannot show subtitles.", this);
        }

        private void OnEnable()
        {
            _camera = Camera.main;
        }

        private void LateUpdate()
        {
            if (!_faceCamera || _camera == null) return;

            // Vector3.up keeps the panel upright: without an explicit up-vector the text rolls with the
            // camera, which is unreadable in VR when the player tilts their head.
            transform.rotation = Quaternion.LookRotation(
                transform.position - _camera.transform.position, Vector3.up);
        }

        /// <summary>Shows a line, revealing it at <see cref="_charactersPerSecond"/>.</summary>
        public void Show(string text) => Display(text, instant: _charactersPerSecond <= 0f);

        /// <summary>Shows a line in full immediately, with no typing.</summary>
        public void ShowInstant(string text) => Display(text, instant: true);

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
            gameObject.SetActive(false);
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

            gameObject.SetActive(true);
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
            _text.text = text;
            _text.maxVisibleCharacters = 0;

            Vector2 preferred = _text.GetPreferredValues(text, _maxWidth, 0f);
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
