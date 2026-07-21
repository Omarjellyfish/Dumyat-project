using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace DumyatRuntime
{
    /// <summary>
    /// End-to-end demo of <see cref="GeminiClient"/> and <see cref="ElevenLabsClient"/>.
    /// Exposes two independent TTS paths:
    /// <list type="number">
    ///   <item><b>Speak custom text</b> — sends the <see cref="_customTtsText"/> field directly to ElevenLabs.</item>
    ///   <item><b>Speak LLM response</b> — first fetches a Gemini reply, then passes it to ElevenLabs.</item>
    /// </list>
    /// Wire the buttons and labels in the Inspector, or leave them empty to get an auto-built
    /// world-space panel (useful when dropping the component into a scene without a canvas).
    /// </summary>
    public class GeminiTestSample : MonoBehaviour
    {
        [Header("Gemini Prompt")]
        [SerializeField, TextArea]
        private string _prompt = "In one short sentence, greet a player who just reached the summit of Mount Dumyat.";

        [Header("ElevenLabs TTS — Custom Text (Option 1)")]
        [SerializeField, TextArea]
        private string _customTtsText = "Welcome to the summit of Mount Dumyat!";
        [SerializeField] private Button _speakCustomButton;

        [Header("ElevenLabs TTS — LLM Response (Option 2)")]
        [SerializeField] private Button _askAndSpeakButton;

        [Header("Shared")]
        [SerializeField] private Button _askGeminiButton;
        [SerializeField] private TMP_Text _responseLabel;
        [SerializeField] private AudioSource _audioSource;

        private const string IdleText = "Press a button below to test Gemini or TTS.";
        private const string WaitingGeminiText = "Asking Gemini…";
        private const string WaitingTtsText = "Synthesising speech…";

        private string _lastGeminiResponse;
        private System.DateTime _promptStartTime;

        // Injected pooled AI services (replace the former GeminiClient/ElevenLabsClient singletons).
        private ILLMService _llm;
        private ITTSService _tts;

        [Inject]
        private void Inject(ILLMService llm, ITTSService tts)
        {
            _llm = llm;
            _tts = tts;
        }

        private void Start()
        {
            if (_askGeminiButton == null || _responseLabel == null || _speakCustomButton == null || _askAndSpeakButton == null)
                BuildFallbackUI();

            if (_responseLabel != null)
                _responseLabel.text = IdleText;

            if (_askGeminiButton != null)
                _askGeminiButton.onClick.AddListener(SendPrompt);

            if (_speakCustomButton != null)
                _speakCustomButton.onClick.AddListener(SpeakCustomText);

            if (_askAndSpeakButton != null)
            {
                _askAndSpeakButton.onClick.AddListener(AskAndSpeak);
                _askAndSpeakButton.interactable = true;
            }
        }

        private void OnDestroy()
        {
            if (_askGeminiButton != null) _askGeminiButton.onClick.RemoveListener(SendPrompt);
            if (_speakCustomButton != null) _speakCustomButton.onClick.RemoveListener(SpeakCustomText);
            if (_askAndSpeakButton != null) _askAndSpeakButton.onClick.RemoveListener(AskAndSpeak);
        }

        // ── Option 1: Speak custom text ─────────────────────────────────────────────────────────

        /// <summary>Sends <see cref="_customTtsText"/> to the pooled TTS service (e.g. ElevenLabs).</summary>
        public void SpeakCustomText()
        {
            if (_tts == null)
            {
                WarnAndDisplay("GeminiTestSample: No ITTSService available (missing DI scope?).");
                return;
            }

            Debug.Log($"GeminiTestSample: Speaking custom text → \"{_customTtsText}\"");
            SetAllInteractable(false);
            SetLabel(WaitingTtsText);
            _tts.PlaySpeech(null, _customTtsText, _audioSource, OnSpeakComplete, OnTtsFailure);
        }

        // ── Option 2: Ask the LLM, then speak the reply ─────────────────────────────────────────

        /// <summary>Sends the LLM prompt and, on success, passes the reply to the TTS service.</summary>
        public async void AskAndSpeak()
        {
            if (_llm == null)
            {
                WarnAndDisplay("GeminiTestSample: No ILLMService available (missing DI scope?).");
                return;
            }

            Debug.Log($"GeminiTestSample: Sending prompt → \"{_prompt}\"");
            SetAllInteractable(false);
            SetLabel(WaitingGeminiText);
            _promptStartTime = System.DateTime.Now;

            string reply = await _llm.GenerateTextAsync(_prompt);
            if (string.IsNullOrEmpty(reply))
            {
                OnGeminiFailure("The language model returned no response.");
                return;
            }

            _lastGeminiResponse = reply;
            double elapsed = System.DateTime.Now.Subtract(_promptStartTime).TotalMilliseconds;
            Debug.Log($"GeminiTestSample: LLM replied in {elapsed:F0} ms → {reply}");
            SetLabel(reply);

            if (_tts == null)
            {
                Debug.LogWarning("GeminiTestSample: No ITTSService — skipping TTS.");
                SetAllInteractable(true);
                return;
            }

            SetLabel($"{reply}\n\n[Synthesising speech…]");
            _tts.PlaySpeech(null, reply, _audioSource, OnSpeakComplete, OnTtsFailure);
        }

        // ── LLM-only (no TTS) ────────────────────────────────────────────────────────────────────

        /// <summary>Sends the configured prompt without TTS. Public so it can be wired to other UI events.</summary>
        public async void SendPrompt()
        {
            if (_llm == null)
            {
                WarnAndDisplay("GeminiTestSample: No ILLMService available (missing DI scope?).");
                return;
            }

            Debug.Log($"GeminiTestSample: Sending prompt → \"{_prompt}\"");
            SetAllInteractable(false);
            SetLabel(WaitingGeminiText);
            _promptStartTime = System.DateTime.Now;

            string reply = await _llm.GenerateTextAsync(_prompt);
            if (string.IsNullOrEmpty(reply))
            {
                OnGeminiFailure("The language model returned no response.");
                return;
            }
            OnGeminiResponse(reply);
        }

        private void OnGeminiResponse(string reply)
        {
            _lastGeminiResponse = reply;
            double elapsed = System.DateTime.Now.Subtract(_promptStartTime).TotalMilliseconds;
            Debug.Log($"GeminiTestSample: LLM replied in {elapsed:F0} ms → {reply}");
            SetLabel(reply);
            SetAllInteractable(true);
        }

        private void OnGeminiFailure(string fallback)
        {
            Debug.LogWarning($"GeminiTestSample: LLM request failed; fallback → {fallback}");
            SetLabel(fallback);
            SetAllInteractable(true);
        }

        // ── Shared TTS callbacks ─────────────────────────────────────────────────────────────────

        // The TTS service plays the clip on our AudioSource itself; this just restores UI once done.
        private void OnSpeakComplete()
        {
            if (!string.IsNullOrEmpty(_lastGeminiResponse))
                SetLabel(_lastGeminiResponse);

            SetAllInteractable(true);
        }

        // Matches ITTSService.PlaySpeech's parameterless onError (the pooled service handles the
        // per-model failure detail internally and only signals "the whole pool failed" here).
        private void OnTtsFailure()
        {
            Debug.LogWarning("GeminiTestSample: TTS failed (no model could synthesise the text).");
            SetLabel("TTS failed.");
            SetAllInteractable(true);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────────────

        private void SetLabel(string text)
        {
            if (_responseLabel != null) _responseLabel.text = text;
        }

        private void SetAllInteractable(bool value)
        {
            if (_askGeminiButton != null)  _askGeminiButton.interactable = value;
            if (_speakCustomButton != null) _speakCustomButton.interactable = value;
            if (_askAndSpeakButton != null) _askAndSpeakButton.interactable = value;
        }

        private void WarnAndDisplay(string message)
        {
            Debug.LogWarning(message);
            SetLabel(message);
        }

        // ── Auto-built world-space UI ────────────────────────────────────────────────────────────

        private void BuildFallbackUI()
        {
            GameObject canvasGO = new GameObject("AI Sample Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
            Canvas canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform canvasRect = (RectTransform)canvasGO.transform;
            canvasRect.sizeDelta = new Vector2(600f, 560f);
            canvasRect.localScale = Vector3.one * 0.0015f;
            PositionInFrontOfCamera(canvasRect);

            // Dark background panel.
            Image panel = CreateUIChild("Panel", canvasRect).AddComponent<Image>();
            panel.color = new Color(0f, 0f, 0f, 0.75f);
            Stretch(panel.rectTransform);

            // Response label (upper portion).
            if (_responseLabel == null)
            {
                GameObject labelGO = CreateUIChild("Response Label", canvasRect);
                TextMeshProUGUI label = labelGO.AddComponent<TextMeshProUGUI>();
                label.fontSize = 22f;
                label.color = Color.white;
                label.alignment = TextAlignmentOptions.TopLeft;
                RectTransform lr = label.rectTransform;
                lr.anchorMin = new Vector2(0.05f, 0.40f);
                lr.anchorMax = new Vector2(0.95f, 0.96f);
                lr.offsetMin = lr.offsetMax = Vector2.zero;
                _responseLabel = label;
            }

            // "Ask Gemini" button.
            if (_askGeminiButton == null)
                _askGeminiButton = BuildButton("Ask Gemini", canvasRect, new Vector2(0.05f, 0.28f), new Vector2(0.48f, 0.40f), new Color(0.16f, 0.42f, 0.78f));

            // "Ask + Speak" button.
            if (_askAndSpeakButton == null)
                _askAndSpeakButton = BuildButton("Ask + Speak", canvasRect, new Vector2(0.52f, 0.28f), new Vector2(0.95f, 0.40f), new Color(0.20f, 0.55f, 0.30f));

            // "Speak Custom" button.
            if (_speakCustomButton == null)
                _speakCustomButton = BuildButton("Speak Custom Text", canvasRect, new Vector2(0.05f, 0.13f), new Vector2(0.95f, 0.25f), new Color(0.60f, 0.35f, 0.10f));

            // Small label showing what the custom text is.
            GameObject hintGO = CreateUIChild("Custom TTS Hint", canvasRect);
            TextMeshProUGUI hint = hintGO.AddComponent<TextMeshProUGUI>();
            hint.text = $"Custom: \"{_customTtsText}\"";
            hint.fontSize = 16f;
            hint.color = new Color(0.8f, 0.8f, 0.8f);
            hint.alignment = TextAlignmentOptions.Center;
            RectTransform hr = hint.rectTransform;
            hr.anchorMin = new Vector2(0.05f, 0.03f);
            hr.anchorMax = new Vector2(0.95f, 0.12f);
            hr.offsetMin = hr.offsetMax = Vector2.zero;

            // Auto-add an AudioSource if none assigned.
            if (_audioSource == null)
                _audioSource = canvasGO.AddComponent<AudioSource>();
        }

        private Button BuildButton(string label, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            GameObject go = CreateUIChild(label + " Button", parent);
            Image img = go.AddComponent<Image>();
            img.color = color;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            GameObject textGO = CreateUIChild("Text", rt);
            TextMeshProUGUI txt = textGO.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 24f;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.Center;
            Stretch(txt.rectTransform);
            return btn;
        }

        private static GameObject CreateUIChild(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void PositionInFrontOfCamera(RectTransform canvasRect)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Transform ct = cam.transform;
                canvasRect.position = ct.position + ct.forward * 2f;
                canvasRect.rotation = Quaternion.LookRotation(canvasRect.position - ct.position, Vector3.up);
            }
            else
            {
                canvasRect.position = new Vector3(0f, 1.5f, 2f);
            }
        }
    }
}
