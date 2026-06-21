using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace DumyatRuntime
{
    /// <summary>
    /// Minimal end-to-end demo of <see cref="GeminiClient"/>: a button fires a hard-coded prompt and
    /// the reply (or fallback message) is logged to the console and shown on a UI label. If no UI is
    /// wired in the Inspector, a small world-space panel is built automatically in front of the player
    /// so the sample works when simply dropped into a scene.
    /// </summary>
    public class GeminiTestSample : MonoBehaviour
    {
        [Header("Prompt")]
        [SerializeField, TextArea]
        private string _prompt = "In one short sentence, greet a player who just reached the summit of Mount Dumyat.";

        [Header("UI (optional — auto-built when left empty)")]
        [SerializeField] private Button _sendButton;
        [SerializeField] private TMP_Text _responseLabel;

        private const string IdleText = "Press \"Ask Gemini\" to send the prompt.";
        private const string WaitingText = "Thinking…";

        private void Start()
        {
            if (_sendButton == null || _responseLabel == null)
                BuildFallbackUI();

            if (_responseLabel != null)
                _responseLabel.text = IdleText;

            if (_sendButton != null)
                _sendButton.onClick.AddListener(SendPrompt);
        }

        private void OnDestroy()
        {
            if (_sendButton != null)
                _sendButton.onClick.RemoveListener(SendPrompt);
        }

        /// <summary>Sends the configured prompt. Public so it can also be wired to other UI events.</summary>
        public void SendPrompt()
        {
            if (GeminiClient.Singleton == null)
            {
                const string message = "GeminiTestSample: No GeminiClient in the scene.";
                Debug.LogWarning(message);
                if (_responseLabel != null) _responseLabel.text = message;
                return;
            }

            Debug.Log($"GeminiTestSample: Sending prompt → \"{_prompt}\"");
            SetInteractable(false);
            if (_responseLabel != null) _responseLabel.text = WaitingText;

            GeminiClient.Singleton.SendPrompt(_prompt, OnResponse, OnFailure);
        }

        private void OnResponse(string reply)
        {
            Debug.Log($"GeminiTestSample: Gemini replied → {reply}");
            if (_responseLabel != null) _responseLabel.text = reply;
            SetInteractable(true);
        }

        private void OnFailure(string fallback)
        {
            Debug.LogWarning($"GeminiTestSample: Request failed; showing fallback → {fallback}");
            if (_responseLabel != null) _responseLabel.text = fallback;
            SetInteractable(true);
        }

        private void SetInteractable(bool value)
        {
            if (_sendButton != null) _sendButton.interactable = value;
        }

        // --- Auto-built world-space UI ---------------------------------------------------------

        private void BuildFallbackUI()
        {
            GameObject canvasGO = new GameObject("Gemini Sample Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
            Canvas canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform canvasRect = (RectTransform)canvasGO.transform;
            canvasRect.sizeDelta = new Vector2(600f, 400f);
            canvasRect.localScale = Vector3.one * 0.0015f;
            PositionInFrontOfCamera(canvasRect);

            // Dark background panel.
            GameObject panelGO = CreateUIChild("Panel", canvasRect);
            Image panel = panelGO.AddComponent<Image>();
            panel.color = new Color(0f, 0f, 0f, 0.75f);
            Stretch(panel.rectTransform);

            // Response label (top portion).
            if (_responseLabel == null)
            {
                GameObject labelGO = CreateUIChild("Response Label", canvasRect);
                TextMeshProUGUI label = labelGO.AddComponent<TextMeshProUGUI>();
                label.fontSize = 24f;
                label.color = Color.white;
                label.alignment = TextAlignmentOptions.TopLeft;
                RectTransform lr = label.rectTransform;
                lr.anchorMin = new Vector2(0.05f, 0.30f);
                lr.anchorMax = new Vector2(0.95f, 0.95f);
                lr.offsetMin = Vector2.zero;
                lr.offsetMax = Vector2.zero;
                _responseLabel = label;
            }

            // Button (bottom portion).
            if (_sendButton == null)
            {
                GameObject buttonGO = CreateUIChild("Ask Button", canvasRect);
                Image buttonImage = buttonGO.AddComponent<Image>();
                buttonImage.color = new Color(0.16f, 0.42f, 0.78f, 1f);
                Button button = buttonGO.AddComponent<Button>();
                button.targetGraphic = buttonImage;
                RectTransform br = (RectTransform)buttonGO.transform;
                br.anchorMin = new Vector2(0.30f, 0.05f);
                br.anchorMax = new Vector2(0.70f, 0.22f);
                br.offsetMin = Vector2.zero;
                br.offsetMax = Vector2.zero;

                GameObject btnTextGO = CreateUIChild("Text", br);
                TextMeshProUGUI btnText = btnTextGO.AddComponent<TextMeshProUGUI>();
                btnText.text = "Ask Gemini";
                btnText.fontSize = 28f;
                btnText.color = Color.white;
                btnText.alignment = TextAlignmentOptions.Center;
                Stretch(btnText.rectTransform);

                _sendButton = button;
            }
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
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
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
