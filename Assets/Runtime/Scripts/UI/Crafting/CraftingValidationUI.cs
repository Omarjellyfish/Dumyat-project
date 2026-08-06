using UnityEngine;
using UnityEngine.UIElements;
using Reflex.Attributes;

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
        [SerializeField] private Color _successColor = new Color(0.0f, 0.9f, 0.46f, 1f);
        [SerializeField] private Color _failColor = new Color(1f, 0.09f, 0.26f, 1f);
        [SerializeField] private Color _idleColor = new Color(0.21f, 0.27f, 0.31f, 1f);

        [Header("Icons")]
        [SerializeField] private Texture2D _successIcon;
        [SerializeField] private Texture2D _failureIcon;
        [SerializeField] private DialogueSequenceSO _afterMakingChairSequence;
        [SerializeField] private DialoguePriority _priority = DialoguePriority.Normal;


        // Panel chrome in both languages. Authored uppercase on the English side because UI Toolkit
        // has no text-transform; casing is a no-op on Arabic.
        private const string EyebrowEn = "INSPECTION";
        private const string EyebrowAr = "الفحص";
        private const string QualityEn = "QUALITY";
        private const string QualityAr = "الجودة";
        private const string FaultsEn = "FAULTS";
        private const string FaultsAr = "العيوب";
        private const string ValidateEn = "VALIDATE BUILD";
        private const string ValidateAr = "افحص الشغل";
        private const string ClearEn = "CLEAR LOG";
        private const string ClearAr = "امسح السجل";
        private const string FaultHeadEn = "FAULT";
        private const string FaultHeadAr = "العيب";
        private const string DetailHeadEn = "DETAIL";
        private const string DetailHeadAr = "التفاصيل";

        private const string ReadyEn = "READY TO VALIDATE";
        private const string ReadyAr = "جاهز للفحص";
        private const string AwaitingEn = "AWAITING CHECK";
        private const string AwaitingAr = "في انتظار الفحص";
        private const string CheckingEn = "VALIDATING BUILD...";
        private const string CheckingAr = "بيراجع الشغل...";
        private const string CheckingSubEn = "CHECKING PARTS";
        private const string CheckingSubAr = "بيفحص القطع";
        private const string PassedEn = "BUILD PASSED";
        private const string PassedAr = "الشغل عدّى";
        private const string FailedEn = "NEEDS WORK";
        private const string FailedAr = "محتاج شغل";
        private const string AllMatchedEn = "ALL PARTS MATCHED";
        private const string AllMatchedAr = "كل القطع مظبوطة";
        private const string AllMatchedRowEn = "All parts matched.";
        private const string AllMatchedRowAr = "كل القطع مظبوطة.";
        private const string PassCodeEn = "PASS";
        private const string PassCodeAr = "تمام";

        private UIDocument _uiDocument;
        private Label _statusTitle;
        private VisualElement _statusIndicator;
        private Label _qualityScore;
        private ScrollView _diagnosticScroll;
        private Button _validateButton;
        private Button _resetButton;

        // Stacked-readout wrappers. The label alone can't be toggled any more — its uppercase
        // caption is a sibling, so the whole block shows and hides together.
        private VisualElement _qualityReadout;
        private VisualElement _issueReadout;
        private Label _issueCount;

        // The tilted verdict block. Its border takes the state colour, so it has to be found too.
        private VisualElement _verdictStamp;
        private Label _stampSub;

        private VisualElement _root;
        private Label _eyebrow;
        private Label _qualityLabel;
        private Label _faultsLabel;
        private Label _faultHeading;
        private Label _detailHeading;

        // The last verdict, kept so a language change re-renders it instead of leaving the player
        // reading the previous language until they validate again.
        private CraftingValidatedEvent _lastResult;
        private bool _hasResult;

        private IEventService _eventService;
        private ISettingsService _settings;

        /// <summary>Whether the panel is currently drawn in Arabic.</summary>
        private bool Arabic => UiLanguage.IsArabic(_settings);

        [Inject]
        private void Inject(IEventService eventService, ISettingsService settings)
        {
            _eventService = eventService;
            _settings = settings;
        }

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
            InitializeUI();
        }

        private void InitializeUI()
        {
            if (_uiDocument == null || _uiDocument.rootVisualElement == null) return;

            var root = _uiDocument.rootVisualElement;
            _root = root;

            _eyebrow = root.Q<Label>("verdict-eyebrow");
            _qualityLabel = root.Q<Label>("quality-label");
            _faultsLabel = root.Q<Label>("faults-label");
            _faultHeading = root.Q<Label>("fault-heading");
            _detailHeading = root.Q<Label>("detail-heading");

            _statusTitle = root.Q<Label>("status-title");
            _statusIndicator = root.Q<VisualElement>("status-indicator");
            _qualityScore = root.Q<Label>("quality-score");
            _qualityReadout = root.Q<VisualElement>("quality-readout");
            _issueReadout = root.Q<VisualElement>("issue-readout");
            _issueCount = root.Q<Label>("issue-count");
            _verdictStamp = root.Q<VisualElement>("verdict-stamp");
            _stampSub = root.Q<Label>("stamp-sub");
            _diagnosticScroll = root.Q<ScrollView>("diagnostic-scroll");
            _validateButton = root.Q<Button>("validate-button");
            _resetButton = root.Q<Button>("reset-button");

            if (_validateButton != null)
                _validateButton.clicked += OnValidateClick;

            if (_resetButton != null)
                _resetButton.clicked += OnResetClick;

            ResetUI();
        }

        /// <summary>
        /// Re-seeds the idle panel once injection has definitely happened. <see cref="Awake"/> builds
        /// the UI, but Reflex may not have supplied <see cref="ISettingsService"/> by then, so that
        /// first draw can fall back to English even when the player chose Arabic.
        /// </summary>
        private void Start()
        {
            if (!_hasResult) ResetUI();
        }

        private void OnEnable()
        {
            _eventService.Subscribe<CraftingValidatedEvent>(HandleValidationResult);
            _eventService.Subscribe<LanguageChangedEvent>(HandleLanguageChanged);
        }

        private void OnDisable()
        {
            _eventService.Unsubscribe<CraftingValidatedEvent>(HandleValidationResult);
            _eventService.Unsubscribe<LanguageChangedEvent>(HandleLanguageChanged);

            if (_validateButton != null)
                _validateButton.clicked -= OnValidateClick;

            if (_resetButton != null)
                _resetButton.clicked -= OnResetClick;
        }

        /// <summary>
        /// Redraws the panel in the newly chosen language. A verdict already on screen is re-rendered
        /// from the stored result rather than cleared — losing a fault list because the player
        /// changed language would be a poor trade.
        /// </summary>
        private void HandleLanguageChanged(LanguageChangedEvent evt)
        {
            ApplyStaticLanguage();

            if (_hasResult) HandleValidationResult(_lastResult);
            else ResetUI();
        }

        /// <summary>Fills the strings authored in the UXML from the current language.</summary>
        private void ApplyStaticLanguage()
        {
            bool ar = Arabic;

            if (_eyebrow != null) _eyebrow.text = ar ? EyebrowAr : EyebrowEn;
            if (_qualityLabel != null) _qualityLabel.text = ar ? QualityAr : QualityEn;
            if (_faultsLabel != null) _faultsLabel.text = ar ? FaultsAr : FaultsEn;
            if (_faultHeading != null) _faultHeading.text = ar ? FaultHeadAr : FaultHeadEn;
            if (_detailHeading != null) _detailHeading.text = ar ? DetailHeadAr : DetailHeadEn;
            if (_validateButton != null) _validateButton.text = ar ? ValidateAr : ValidateEn;
            if (_resetButton != null) _resetButton.text = ar ? ClearAr : ClearEn;
        }

        private void OnValidateClick()
        {
            if (_station == null)
            {
                Debug.LogWarning("[CraftingValidationUI] Crafting Station reference is missing.", this);
                return;
            }

            bool ar = Arabic;

            if (_statusTitle != null)
            {
                _statusTitle.text = ar ? CheckingAr : CheckingEn;
                _statusTitle.style.color = StyleKeyword.Null;
            }

            if (_stampSub != null)
            {
                _stampSub.text = ar ? CheckingSubAr : CheckingSubEn;
                _stampSub.style.color = StyleKeyword.Null;
            }

            ClearStampBorder();

            if (_statusIndicator != null) _statusIndicator.style.backgroundColor = _idleColor;

            SetReadouts(false, false);
            UiLanguage.ApplyScript(_root, ar);

            _station.CheckBuild();
        }

        private void OnResetClick()
        {
            _hasResult = false;
            ResetUI();
        }

        private void HandleValidationResult(CraftingValidatedEvent evt)
        {
            ValidationResult result = evt.Result;

            // Held so a language change can re-render this same verdict.
            _lastResult = evt;
            _hasResult = true;

            ClearDiagnosticsList();

            bool ar = Arabic;
            Color stateColor = result.IsSuccess ? _successColor : _failColor;
            int faultCount = result.FailedParts != null ? result.FailedParts.Count : 0;

            if (_statusTitle != null)
            {
                _statusTitle.text = result.IsSuccess
                    ? (ar ? PassedAr : PassedEn)
                    : (ar ? FailedAr : FailedEn);
                // The verdict carries the state colour too, not just the indicator bar — it is the
                // one thing that has to read from across the room.
                _statusTitle.style.color = stateColor;
            }

            if (_stampSub != null)
            {
                // Arabic keeps one phrasing for every count: the dual and plural forms would each
                // need their own string, and "فيه N عيب" reads correctly throughout.
                _stampSub.text = result.IsSuccess
                    ? (ar ? AllMatchedAr : AllMatchedEn)
                    : ar
                        ? $"فيه {faultCount} عيب"
                        : (faultCount == 1 ? "1 FAULT FOUND" : $"{faultCount} FAULTS FOUND");
                _stampSub.style.color = stateColor;
            }

            SetStampBorder(stateColor);

            if (_statusIndicator != null)
                _statusIndicator.style.backgroundColor = stateColor;

            if (_qualityScore != null)
            {
                if (result.IsSuccess)
                {
                    _qualityScore.text = $"{result.QualityScore * 100f:F0}%";
                    _eventService.Publish(new PlayDialogueEvent(_afterMakingChairSequence, _priority));
                }
            }

            if (_issueCount != null)
                _issueCount.text = faultCount.ToString("00");

            SetReadouts(result.IsSuccess, !result.IsSuccess);

            if (_diagnosticScroll != null && _diagnosticItemTemplate != null)
            {
                if (result.IsSuccess)
                {
                    AddDiagnosticItem(ar ? AllMatchedRowAr : AllMatchedRowEn, true, ar ? PassCodeAr : PassCodeEn);
                }
                else
                {
                    if (result.FailedParts == null || result.FailedParts.Count == 0)
                    {
                        AddDiagnosticItem(result.Message, false, ar ? FaultHeadAr : FaultHeadEn);
                    }
                    else
                    {
                        foreach (var failure in result.FailedParts)
                        {
                            // The kind now lives in its own cell instead of being prefixed onto the
                            // sentence, so the detail text reads as a sentence again.
                            //
                            // NOTE: failure.Details is composed by CraftingValidator and is still
                            // English-only. The code cell beside it is translated, so an Arabic
                            // player gets the fault KIND in Arabic and the measurement sentence in
                            // English until the validator's message building is localised too.
                            AddDiagnosticItem(failure.Details, false, GetReasonCode(failure.Reason));
                        }
                    }
                }
            }

            UiLanguage.ApplyScript(_root, ar);
        }

        /// <summary>
        /// The failure kind as a short word for the row's code cell. Words rather than the old
        /// unicode arrows: an arrow cannot distinguish "too big" from "not attached", and at the VR
        /// type floor a four-letter code is no wider than the glyph was.
        /// </summary>
        private string GetReasonCode(FailReason reason)
        {
            if (Arabic)
            {
                switch (reason)
                {
                    case FailReason.Oversized: return "كبيرة";
                    case FailReason.Undersized: return "صغيرة";
                    case FailReason.OffCenter: return "مزحلقة";
                    case FailReason.MissingPart: return "ناقصة";
                    case FailReason.NoSlotMatch: return "زايدة";
                    default: return FaultHeadAr;
                }
            }

            switch (reason)
            {
                case FailReason.Oversized: return "OVER";
                case FailReason.Undersized: return "UNDER";
                case FailReason.OffCenter: return "OFFSET";
                case FailReason.MissingPart: return "ABSENT";
                case FailReason.NoSlotMatch: return "EXTRA";
                default: return "FAULT";
            }
        }

        private void AddDiagnosticItem(string message, bool isSuccess, string reasonCode = null)
        {
            VisualElement itemRoot = _diagnosticItemTemplate.Instantiate();
            _diagnosticScroll.Add(itemRoot);

            var controller = new CraftingDiagnosticItemUI(itemRoot);
            Color itemColor = isSuccess ? _successColor : _failColor;
            Texture2D icon = isSuccess ? _successIcon : _failureIcon;

            controller.Setup(message, itemColor, icon, reasonCode);
        }

        private void ResetUI()
        {
            ClearDiagnosticsList();

            bool ar = Arabic;
            ApplyStaticLanguage();

            if (_statusTitle != null)
            {
                _statusTitle.text = ar ? ReadyAr : ReadyEn;
                // Back to the USS default rather than a stale pass/fail colour.
                _statusTitle.style.color = StyleKeyword.Null;
            }

            if (_stampSub != null)
            {
                _stampSub.text = ar ? AwaitingAr : AwaitingEn;
                _stampSub.style.color = StyleKeyword.Null;
            }

            ClearStampBorder();

            if (_statusIndicator != null) _statusIndicator.style.backgroundColor = _idleColor;

            SetReadouts(false, false);
            UiLanguage.ApplyScript(_root, ar);
        }

        /// <summary>Tints all four stamp borders. UI Toolkit has no shorthand for border colour.</summary>
        private void SetStampBorder(Color color)
        {
            if (_verdictStamp == null) return;

            _verdictStamp.style.borderTopColor = color;
            _verdictStamp.style.borderRightColor = color;
            _verdictStamp.style.borderBottomColor = color;
            _verdictStamp.style.borderLeftColor = color;
        }

        private void ClearStampBorder()
        {
            if (_verdictStamp == null) return;

            _verdictStamp.style.borderTopColor = StyleKeyword.Null;
            _verdictStamp.style.borderRightColor = StyleKeyword.Null;
            _verdictStamp.style.borderBottomColor = StyleKeyword.Null;
            _verdictStamp.style.borderLeftColor = StyleKeyword.Null;
        }

        /// <summary>
        /// Shows at most one of the two stacked readouts — quality on a pass, issue count on a fail,
        /// neither while idle. Null-guarded so the panel still works against a UXML without them.
        /// </summary>
        private void SetReadouts(bool showQuality, bool showIssues)
        {
            if (_qualityReadout != null)
                _qualityReadout.style.display = showQuality ? DisplayStyle.Flex : DisplayStyle.None;

            if (_issueReadout != null)
                _issueReadout.style.display = showIssues ? DisplayStyle.Flex : DisplayStyle.None;

            // Kept in sync for any UXML that still places quality-score outside a wrapper.
            if (_qualityReadout == null && _qualityScore != null)
                _qualityScore.style.display = showQuality ? DisplayStyle.Flex : DisplayStyle.None;
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
