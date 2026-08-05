using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The credits, cut into the workbench — one name per line, each in a slightly different hand.
    ///
    /// The jitter is the whole point. Four names typeset on a shared baseline read as a caption
    /// printed on wood; the same four rotated a degree either way, at slightly different sizes and
    /// depths, read as four people who each cut their name in at a different time. Perfectly aligned
    /// carving is the one thing that would give it away.
    ///
    /// The carved look itself is a dark fill with a LIGHT edge offset downward — the lit lower lip of
    /// a gouge — applied through <see cref="TmpEngraving"/>, the same helper the branded nail labels
    /// use for their shadow. Same feature, opposite colour, opposite story.
    /// </summary>
    public class CarvedCredits : MonoBehaviour
    {
        [Header("Names")]
        [Tooltip("One carved line each, in order. Editing this list is the whole maintenance story " +
                 "— nothing else needs touching to add or remove a name.")]
        [SerializeField]
        private List<string> _names = new List<string>
        {
            "Abdelrahman Hassan",
            "Omar Kandil",
            "Ahmed Ashraf",
            "Mohab Fawzy"
        };

        [Tooltip("Cut under the names, smaller and shallower — the year the bench was signed. " +
                 "Leave empty to omit it.")]
        [SerializeField] private string _footer = "DUMYAT · 2026";

        [Header("Type")]
        [Tooltip("Font for the names. A hand-cut script sells this far better than a grotesque; " +
                 "see the note in the class summary about what this project currently has.")]
        [SerializeField] private TMP_FontAsset _font;

        [SerializeField] private float _fontSize = 44f;
        [SerializeField] private float _footerFontSize = 20f;
        [SerializeField] private float _lineStepUnits = 72f;

        [Header("Cut")]
        [Tooltip("The wood inside the cut — nearly black, because a gouge is in shadow.")]
        [SerializeField] private Color _cutColor = new Color(0.071f, 0.035f, 0.008f, 1f);   // #120906

        [Tooltip("Light catching the lower lip of the cut. Pale and warm, like the workshop lamp.")]
        [SerializeField] private Color _lipColor = new Color(1f, 0.886f, 0.690f, 0.42f);    // #FFE2B0

        [Tooltip("Which way the lip is lit. Positive Y throws the highlight BELOW the stroke, which " +
                 "is what makes it read as cut in rather than raised out.")]
        [SerializeField] private Vector2 _lipOffset = new Vector2(0f, -0.55f);

        [Range(0f, 1f)] [SerializeField] private float _lipDilate = 0.08f;
        [Range(0f, 1f)] [SerializeField] private float _lipSoftness = 0.12f;

        [Header("Hand")]
        [Tooltip("Maximum tilt either way, in degrees. Past about 2 it stops reading as an unsteady " +
                 "hand and starts reading as a mistake.")]
        [SerializeField] private float _tiltDegrees = 1.4f;

        [Tooltip("Sideways wander, in the parent canvas's units.")]
        [SerializeField] private float _driftUnits = 14f;

        [Tooltip("How much bigger or smaller any one name may be. 0.1 = up to 10% either way.")]
        [Range(0f, 0.3f)] [SerializeField] private float _sizeVariance = 0.09f;

        [Tooltip("Fixed so the bench carves the same way every launch. Change it to reshuffle the " +
                 "hands; a random seed would make the credits restless between sessions.")]
        [SerializeField] private int _seed = 7;

        private readonly List<TMP_Text> _lines = new List<TMP_Text>();

        /// <summary>The carved lines, for anything that needs to reveal or measure them.</summary>
        public IReadOnlyList<TMP_Text> Lines => _lines;

        private void Awake() => Build();

        private void Build()
        {
            if (_font == null)
            {
                Debug.LogWarning($"{nameof(CarvedCredits)} on '{name}' has no font assigned — " +
                                 "no names will be cut.", this);
                return;
            }

            // A fixed-seed generator rather than UnityEngine.Random: this must not disturb, or be
            // disturbed by, whatever else is drawing random numbers this frame.
            var hand = new System.Random(_seed);

            int index = 0;
            foreach (string entry in _names)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                Cut(entry, index++, _fontSize, hand, isFooter: false);
            }

            if (!string.IsNullOrWhiteSpace(_footer))
                Cut(_footer, index, _footerFontSize, hand, isFooter: true);
        }

        private void Cut(string text, int index, float size, System.Random hand, bool isFooter)
        {
            var go = new GameObject($"Cut_{index}", typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = _font;
            label.text = text;
            label.color = _cutColor;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;

            float wobble = isFooter ? 0.35f : 1f;   // the year is stamped more carefully than a name
            float scale = 1f + (float)(hand.NextDouble() * 2 - 1) * _sizeVariance * wobble;
            label.fontSize = size * scale;

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(560f, size * 1.6f);

            float drift = (float)(hand.NextDouble() * 2 - 1) * _driftUnits * wobble;
            float top = (_names.Count - 1) * 0.5f * _lineStepUnits;
            rect.anchoredPosition = new Vector2(drift, top - index * _lineStepUnits);

            float tilt = (float)(hand.NextDouble() * 2 - 1) * _tiltDegrees * wobble;
            rect.localRotation = Quaternion.Euler(0f, 0f, tilt);

            // fontMaterial forks a per-label instance, so each name can be cut to its own depth.
            TmpEngraving.TrySetUnderlay(label.fontMaterial, _lipColor, _lipOffset,
                                        _lipDilate, _lipSoftness);
            TmpEngraving.Repad(label);

            _lines.Add(label);
        }
    }
}
