using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Reflex.Attributes;
using TMPro;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// A sale "summary paper" that sits beside the merchant's subtitle panel during a haggle. It appears
    /// only while the merchant is interacting (mirrors the subtitle's show/hide), heads the page with the
    /// catch being sold, and keeps a running, ink-typed log of every price the merchant CALLS OUT — never
    /// the hidden fair value, and never a number he has not voiced.
    ///
    /// Each new price is typed in beneath the last; when a price is superseded it is struck through and the
    /// new one typed below it. When the page fills, it is torn off and a fresh one begins from the latest
    /// price. Driven purely by merchant events: <see cref="MerchantSpokeEvent"/> (show),
    /// <see cref="MerchantStoppedSpeakingEvent"/> (hide), <see cref="MerchantOfferedPriceEvent"/> (log a
    /// price), and <see cref="ScaleContentsChangedEvent"/> (refresh the catch header).
    ///
    /// Note: the shared TMP font (LiberationSans) carries no Arabic glyphs, so the paper stays Latin —
    /// species counts and weights only, currency shown as "EGP".
    /// </summary>
    public class SummaryCard : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The scale being sold from. Auto-resolved from a parent, else the owning merchant's scale.")]
        [SerializeField] private FishSellScale _scale;

        [Tooltip("The merchant this paper accompanies. Auto-resolved from a parent if left empty.")]
        [SerializeField] private MerchantNPC _merchant;

        [Tooltip("Letterhead + catch summary (updated instantly). Auto-resolved from children if empty.")]
        [SerializeField] private TextMeshProUGUI _headerText;

        [Tooltip("The ink-typed price log. Auto-resolved from children if empty.")]
        [SerializeField] private TextMeshProUGUI _logText;

        [Tooltip("Static torn-edge rule printed at the TOP of the card, above the letterhead. Its own " +
                 "element (separate from the title) so the title can carry a larger font than the rule. " +
                 "Optional.")]
        [SerializeField] private TextMeshProUGUI _headerBorderText;

        [Tooltip("Static torn-edge rule printed at the bottom of the card. Optional.")]
        [SerializeField] private TextMeshProUGUI _footerText;

        [Tooltip("Paper visuals shown only while the merchant is interacting. Keep this SEPARATE from the " +
                 "GameObject holding this component so the event subscription stays alive.")]
        [SerializeField] private GameObject _visualRoot;

        [Tooltip("Objects revealed only while at least one fish rests on the scale (e.g. a 'Fresh' stamp). " +
                 "Hidden when the scale is empty. Add any number — they are toggled together.")]
        [SerializeField] private List<GameObject> _shownWhenFishPlaced = new List<GameObject>();

        [Header("Typing")]
        [Tooltip("Ink-in speed for newly logged prices, in characters per second. 0 shows them at once.")]
        [SerializeField, Min(0f)] private float _charactersPerSecond = 34f;

        [Header("Page")]
        [Tooltip("How many price lines fit before the page is torn off and a fresh one starts.")]
        [SerializeField, Min(1)] private int _maxLines = 6;

        [Header("Formatting")]
        [Tooltip("Printed at the top of the page as a letterhead.")]
        [SerializeField] private string _letterhead = "EL MIZAN";

        [Tooltip("Currency label appended to each price. Arabic will not render in the shared font.")]
        [SerializeField] private string _currencySuffix = "EGP";

        [Tooltip("Torn-edge rule drawn at the top of the header and the bottom of the card, so it reads " +
                 "as a real paper receipt.")]
        [SerializeField] private string _borderLine = "----------------";

        [Header("Facing")]
        [Tooltip("Billboard toward the camera each frame, staying upright.")]
        [SerializeField] private bool _faceCamera = true;

        private IEventService _events;
        private Camera _camera;

        // Prices the merchant has called out, in order. All but the last are shown struck through.
        private readonly List<float> _offers = new List<float>();
        private int _revealed;              // visible characters of the log already inked in
        private Coroutine _typing;
        private bool _shown;

        [Inject]
        private void Inject(IEventService events) => _events = events;

        private void Awake()
        {
            if (_merchant == null) _merchant = GetComponentInParent<MerchantNPC>();
            if (_headerText == null || _logText == null)
            {
                var texts = GetComponentsInChildren<TextMeshProUGUI>(true);
                if (_headerText == null && texts.Length > 0) _headerText = texts[0];
                if (_logText == null && texts.Length > 1) _logText = texts[1];
            }

            // The scale is normally an ancestor; fall back to the owning merchant's scale so the paper
            // works whether it is parented under the scale or beside the merchant's subtitle.
            if (_scale == null) _scale = GetComponentInParent<FishSellScale>();
            if (_scale == null && _merchant != null) _scale = _merchant.SellScale as FishSellScale;

            if (_logText == null)
                Debug.LogWarning($"{nameof(SummaryCard)} on '{name}' has no log TextMeshProUGUI — " +
                                 "it cannot show prices.", this);
        }

        private void OnEnable()
        {
            _camera = Camera.main;
            HidePaper();   // stays hidden until the merchant starts speaking

            if (_events == null)
            {
                Debug.LogWarning($"{nameof(SummaryCard)} on '{name}' has no IEventService injected " +
                                 "— the paper will not update. Does this scene have a ContainerScope?", this);
                return;
            }

            _events.Subscribe<MerchantSpokeEvent>(HandleSpoke);
            _events.Subscribe<MerchantStoppedSpeakingEvent>(HandleStopped);
            _events.Subscribe<MerchantOfferedPriceEvent>(HandleOffer);
            _events.Subscribe<ScaleContentsChangedEvent>(HandleScaleChanged);
        }

        private void OnDisable()
        {
            if (_events == null) return;
            _events.Unsubscribe<MerchantSpokeEvent>(HandleSpoke);
            _events.Unsubscribe<MerchantStoppedSpeakingEvent>(HandleStopped);
            _events.Unsubscribe<MerchantOfferedPriceEvent>(HandleOffer);
            _events.Unsubscribe<ScaleContentsChangedEvent>(HandleScaleChanged);
        }

        private void LateUpdate()
        {
            if (!_faceCamera || _camera == null) return;

            // Vector3.up keeps the paper upright so it never rolls with the player's head tilt in VR.
            transform.rotation = Quaternion.LookRotation(
                transform.position - _camera.transform.position, Vector3.up);
        }

        // Every merchant shares the channel — only react to our own merchant (or any, if unresolved).
        private bool IsMine(MerchantNPC merchant) => _merchant == null || merchant == _merchant;

        private void HandleSpoke(MerchantSpokeEvent evt)
        {
            if (!IsMine(evt.Merchant)) return;
            if (!_shown) ShowPaper();
        }

        private void HandleStopped(MerchantStoppedSpeakingEvent evt)
        {
            if (!IsMine(evt.Merchant)) return;
            HidePaper();
        }

        private void HandleOffer(MerchantOfferedPriceEvent evt)
        {
            if (!ReferenceEquals(evt.Scale, _scale)) return;
            if (!_shown) ShowPaper();   // safety: raise the paper if a price arrives first
            LogPrice(evt.Amount);
        }

        private void HandleScaleChanged(ScaleContentsChangedEvent evt)
        {
            if (!_shown || !ReferenceEquals(evt.Scale, _scale)) return;
            UpdateHeader();               // the catch changed mid-haggle — keep the letterhead honest
            RefreshFishPlacedVisuals();   // …and reveal/hide the fish-placed-only visuals (e.g. Fresh stamp)
        }

        private void ShowPaper()
        {
            _shown = true;
            StopTyping();
            _offers.Clear();
            _revealed = 0;
            if (_visualRoot != null) _visualRoot.SetActive(true);
            UpdateHeader();
            RefreshFishPlacedVisuals();
            if (_logText != null)
            {
                _logText.text = string.Empty;
                _logText.maxVisibleCharacters = 0;
            }
        }

        private void HidePaper()
        {
            _shown = false;
            StopTyping();
            _offers.Clear();
            _revealed = 0;
            if (_visualRoot != null) _visualRoot.SetActive(false);
        }

        private void UpdateHeader()
        {
            // Torn-edge rules are their own elements (see _headerBorderText / _footerText) so the title can
            // be sized independently of the thin border font.
            if (_headerBorderText != null) _headerBorderText.text = _borderLine;   // top torn edge
            if (_footerText != null) _footerText.text = _borderLine;               // bottom torn edge

            if (_headerText == null) return;

            int count = _scale != null ? _scale.FishOnScale.Count : 0;
            float kg = _scale != null ? _scale.TotalWeightKg : 0f;

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(_letterhead)) sb.Append(_letterhead).Append('\n');
            sb.Append(count).Append(count == 1 ? " fish, " : " fish, ")
              .Append(kg.ToString("0.00", CultureInfo.InvariantCulture)).Append(" kg");
            _headerText.text = sb.ToString();
        }

        // Reveals the fish-placed-only visuals (e.g. the Fresh stamp) when the scale carries at least one
        // fish, hides them otherwise. Idempotent — safe to call on show and on every scale change.
        private void RefreshFishPlacedVisuals()
        {
            bool hasFish = _scale != null && _scale.FishOnScale.Count > 0;
            for (int i = 0; i < _shownWhenFishPlaced.Count; i++)
            {
                GameObject go = _shownWhenFishPlaced[i];
                if (go != null && go.activeSelf != hasFish) go.SetActive(hasFish);
            }
        }

        // Records a freshly called-out price. Ignores a repeat of the standing price so an unchanged offer
        // (e.g. a hold-firm re-read) does not spawn a duplicate struck line.
        private void LogPrice(float amount)
        {
            float rounded = Mathf.Round(amount);
            if (rounded <= 0f) return;
            if (_offers.Count > 0 && Mathf.Approximately(_offers[_offers.Count - 1], rounded)) return;

            _offers.Add(rounded);

            // Page full → tear it off: keep only the latest price on a fresh sheet and ink it from scratch.
            if (_offers.Count > _maxLines)
            {
                _offers.Clear();
                _offers.Add(rounded);
                _revealed = 0;
            }

            RenderLog();
        }

        private void RenderLog()
        {
            if (_logText == null) return;

            var sb = new StringBuilder();
            for (int i = 0; i < _offers.Count; i++)
            {
                string line = _offers[i].ToString("0", CultureInfo.InvariantCulture) + " " + _currencySuffix;
                bool superseded = i < _offers.Count - 1;   // struck through once a newer price is called
                if (superseded) sb.Append("<s>").Append(line).Append("</s>\n");
                else sb.Append(line);
            }

            string rendered = sb.ToString();
            _logText.text = rendered;

            // Count reveal length from the string, NOT from _logText.textInfo.characterCount: on the frame
            // the paper is first activated TMP has not laid out yet and reports 0, which silently swallowed
            // the very first called-out price (later changes worked because the paper was already laid out).
            int total = CountVisibleCharacters(rendered);

            // Keep everything already inked; type only the newly added tail. Struck-through tags are not
            // visible characters, so striking an earlier line never shifts the reveal boundary.
            _revealed = Mathf.Clamp(_revealed, 0, total);
            StartTyping(total);
        }

        // The number of characters TMP will reveal via maxVisibleCharacters: every character except those
        // inside a rich-text tag (e.g. <s>...</s>). Spaces and newlines count, mirroring what
        // textInfo.characterCount would report once laid out — but without depending on that layout.
        private static int CountVisibleCharacters(string s)
        {
            int count = 0;
            bool inTag = false;
            foreach (char c in s)
            {
                if (inTag) { if (c == '>') inTag = false; continue; }
                if (c == '<') { inTag = true; continue; }
                count++;
            }
            return count;
        }

        private void StartTyping(int total)
        {
            StopTyping();
            _logText.maxVisibleCharacters = _revealed;

            if (_charactersPerSecond <= 0f || _revealed >= total)
            {
                _revealed = total;
                _logText.maxVisibleCharacters = total;
                return;
            }

            _typing = StartCoroutine(TypeTo(total));
        }

        private IEnumerator TypeTo(int total)
        {
            float shown = _revealed;
            while (shown < total)
            {
                shown += _charactersPerSecond * Time.deltaTime;
                _revealed = Mathf.Min(total, Mathf.FloorToInt(shown));
                _logText.maxVisibleCharacters = _revealed;
                yield return null;
            }

            _revealed = total;
            _logText.maxVisibleCharacters = total;
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
