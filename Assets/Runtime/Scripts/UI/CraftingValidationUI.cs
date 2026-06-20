using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    [RequireComponent(typeof(UIDocument))]
    public class CraftingValidationUI : MonoBehaviour
    {
        [Header("Station Reference")]
        [SerializeField] private CraftingStation _station;

        [Header("Templates")]
        [SerializeField] private VisualTreeAsset _diagnosticItemTemplate;

        [Header("Visual Styling")]
        [SerializeField] private Color _successColor = new Color(0.0f, 0.9f, 0.46f, 1f); // #00E676
        [SerializeField] private Color _failColor = new Color(1f, 0.09f, 0.26f, 1f); // #FF1744
        [SerializeField] private Color _idleColor = new Color(0.21f, 0.27f, 0.31f, 1f); // #37474F
        
        [Header("Icons")]
        [SerializeField] private Texture2D _successIcon;
        [SerializeField] private Texture2D _failureIcon;

        private UIDocument _uiDocument;
        private Label _statusTitle;
        private VisualElement _statusIndicator;
        private Label _qualityScore;
        private ScrollView _diagnosticScroll;
        private Button _validateButton;
        private Button _resetButton;

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
            InitializeUI();
        }

        private void InitializeUI()
        {
            if (_uiDocument == null || _uiDocument.rootVisualElement == null) return;

            var root = _uiDocument.rootVisualElement;

            _statusTitle = root.Q<Label>("status-title");
            _statusIndicator = root.Q<VisualElement>("status-indicator");
            _qualityScore = root.Q<Label>("quality-score");
            _diagnosticScroll = root.Q<ScrollView>("diagnostic-scroll");
            _validateButton = root.Q<Button>("validate-button");
            _resetButton = root.Q<Button>("reset-button");

            if (_validateButton != null)
                _validateButton.clicked += OnValidateClick;

            if (_resetButton != null)
                _resetButton.clicked += OnResetClick;

            ResetUI();
        }

        private void OnEnable()
        {
            CraftingValidator.OnValidationComplete += HandleValidationResult;
        }

        private void OnDisable()
        {
            CraftingValidator.OnValidationComplete -= HandleValidationResult;
            
            if (_validateButton != null)
                _validateButton.clicked -= OnValidateClick;

            if (_resetButton != null)
                _resetButton.clicked -= OnResetClick;
        }

        private void OnValidateClick()
        {
            if (_station == null)
            {
                Debug.LogWarning("[CraftingValidationUI] Crafting Station reference is missing.", this);
                return;
            }

            if (_statusTitle != null) _statusTitle.text = "VALIDATING BUILD...";
            if (_statusIndicator != null) _statusIndicator.style.backgroundColor = _idleColor;

            _station.CheckBuild();
        }

        private void OnResetClick()
        {
            ResetUI();
        }

        private void HandleValidationResult(ValidationResult result)
        {
            ClearDiagnosticsList();

            if (_statusTitle != null)
                _statusTitle.text = result.IsSuccess ? "BUILD SUCCESS" : "BUILD FAILED";

            if (_statusIndicator != null)
                _statusIndicator.style.backgroundColor = result.IsSuccess ? _successColor : _failColor;

            if (_qualityScore != null)
            {
                if (result.IsSuccess)
                {
                    _qualityScore.text = $"Quality: {result.QualityScore * 100f:F1}%";
                    _qualityScore.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _qualityScore.style.display = DisplayStyle.None;
                }
            }

            if (_diagnosticScroll != null && _diagnosticItemTemplate != null)
            {
                if (result.IsSuccess)
                {
                    AddDiagnosticItem("Assembly verified successfully!", true);
                }
                else
                {
                    foreach (var failure in result.FailedParts)
                    {
                        string message = $"[{failure.Reason}] {failure.Details}";
                        AddDiagnosticItem(message, false);
                    }
                }
            }
        }

        private void AddDiagnosticItem(string message, bool isSuccess)
        {
            VisualElement itemRoot = _diagnosticItemTemplate.Instantiate();
            _diagnosticScroll.Add(itemRoot);

            var controller = new CraftingDiagnosticItemUI(itemRoot);
            Color itemColor = isSuccess ? _successColor : _failColor;
            Texture2D icon = isSuccess ? _successIcon : _failureIcon;
            
            controller.Setup(message, itemColor, icon);
        }

        private void ResetUI()
        {
            ClearDiagnosticsList();

            if (_statusTitle != null) _statusTitle.text = "READY TO VALIDATE";
            if (_qualityScore != null) _qualityScore.style.display = DisplayStyle.None;
            if (_statusIndicator != null) _statusIndicator.style.backgroundColor = _idleColor;
        }

        private void ClearDiagnosticsList()
        {
            if (_diagnosticScroll != null)
            {
                _diagnosticScroll.Clear();
            }
        }
    }
}
