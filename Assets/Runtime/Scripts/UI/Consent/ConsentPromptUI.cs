using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// UI Toolkit prompt for the microphone + internet consent required by the AI merchant
    /// pipeline. Listens for <see cref="ConsentManager.OnConsentUIRequested"/> (fired by
    /// <see cref="ConsentManager.RequestConsent"/>) and can also show itself on startup when
    /// the player has never been asked. The player's choice is reported back through
    /// <see cref="ConsentManager.SubmitConsentResult"/>, which persists it and completes any
    /// pending request callback.
    ///
    /// Expects the ConsentPrompt.uxml layout on the attached <see cref="UIDocument"/>: a
    /// "consent-panel" root, an ALLOW/BLOCK button pair per permission, and
    /// "confirm-button"/"decline-button".
    ///
    /// ALLOW/BLOCK rather than checkboxes, matching Settings ▸ Permissions. A checkbox has no
    /// visible "no": declining meant leaving something unticked and hoping, and Confirm had to be
    /// disabled until both were ticked to make the all-or-nothing rule legible. Two labelled buttons
    /// state both answers, so refusing is an action rather than an omission — and Confirm can simply
    /// submit whatever was chosen.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ConsentPromptUI : MonoBehaviour
    {
        // Element names in ConsentPrompt.uxml.
        private const string PanelName = "consent-panel";
        private const string MicAllowName = "mic-allow-button";
        private const string MicBlockName = "mic-block-button";
        private const string AiAllowName = "ai-allow-button";
        private const string AiBlockName = "ai-block-button";
        private const string ConfirmButtonName = "confirm-button";
        private const string DeclineButtonName = "decline-button";

        private const string SelectedSegmentClass = "consent-seg-btn--selected";

        [Tooltip("Show the prompt automatically on startup when the player has never been asked " +
                 "(either consent still NotAsked). Leave on so consent is settled before the first " +
                 "merchant approach instead of mid-negotiation.")]
        [SerializeField] private bool _promptOnStartWhenNotAsked = true;

        [Tooltip("Placement solver on this GameObject. Auto-resolved if empty.")]
        [SerializeField] private LazyFollowAnchor _followAnchor;

        // Injected consent service (replaces the former _consent).
        private IConsentService _consent;

        [Inject]
        private void Inject(IConsentService consent) => _consent = consent;

        private UIDocument _document;
        private VisualElement _panel;
        private Button _micAllow;
        private Button _micBlock;
        private Button _aiAllow;
        private Button _aiBlock;
        private Button _confirmButton;
        private Button _declineButton;

        // What the player has picked on the panel, before they press CONFIRM. Not read back off the
        // buttons: a class is a rendering detail, and the answer to "may we use your microphone"
        // should not be inferred from what a stylesheet happens to have applied.
        private bool _micAllowed = true;
        private bool _aiAllowed = true;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            if (_followAnchor == null) _followAnchor = GetComponent<LazyFollowAnchor>();
        }

        private void OnEnable()
        {
            VisualElement root = _document.rootVisualElement;
            _panel = root?.Q<VisualElement>(PanelName);
            if (_panel == null)
            {
                Debug.LogWarning("ConsentPromptUI: 'consent-panel' not found — is ConsentPrompt.uxml " +
                                 "assigned as the UIDocument's Source Asset?");
                return;
            }

            _micAllow = root.Q<Button>(MicAllowName);
            _micBlock = root.Q<Button>(MicBlockName);
            _aiAllow = root.Q<Button>(AiAllowName);
            _aiBlock = root.Q<Button>(AiBlockName);
            _confirmButton = root.Q<Button>(ConfirmButtonName);
            _declineButton = root.Q<Button>(DeclineButtonName);

            if (_confirmButton != null) _confirmButton.clicked += HandleConfirm;
            if (_declineButton != null) _declineButton.clicked += HandleDeclineAll;

            if (_micAllow != null) _micAllow.clicked += HandleMicAllow;
            if (_micBlock != null) _micBlock.clicked += HandleMicBlock;
            if (_aiAllow != null) _aiAllow.clicked += HandleAiAllow;
            if (_aiBlock != null) _aiBlock.clicked += HandleAiBlock;

            Hide();

            // Awake/OnEnable order across objects is nondeterministic, so the manager may not
            // exist yet here — Start() (guaranteed after every Awake) retries the subscription.
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (_consent != null)
                _consent.OnConsentUIRequested -= Show;

            if (_confirmButton != null) _confirmButton.clicked -= HandleConfirm;
            if (_declineButton != null) _declineButton.clicked -= HandleDeclineAll;

            if (_micAllow != null) _micAllow.clicked -= HandleMicAllow;
            if (_micBlock != null) _micBlock.clicked -= HandleMicBlock;
            if (_aiAllow != null) _aiAllow.clicked -= HandleAiAllow;
            if (_aiBlock != null) _aiBlock.clicked -= HandleAiBlock;
        }

        private void Start()
        {
            TrySubscribe();
            if (_consent == null)
            {
                Debug.LogWarning("ConsentPromptUI: no IConsentService injected, so the prompt will never " +
                                 "show. It is a ROOT binding (GameInstaller), not a scene object — so " +
                                 "the thing to check is whether this scene has a ContainerScope.", this);
                return;
            }

            if (!_promptOnStartWhenNotAsked || _panel == null) return;

            ConsentState state = _consent.GetConsentStatus();
            if (state.MicConsent == ConsentStatus.NotAsked || state.InternetConsent == ConsentStatus.NotAsked)
                Show();
        }

        // Idempotent subscribe (remove-then-add) so OnEnable and Start can both call it safely.
        private void TrySubscribe()
        {
            if (_consent == null) return;
            _consent.OnConsentUIRequested -= Show;
            _consent.OnConsentUIRequested += Show;
        }

        /// <summary>Shows the prompt, pre-checking the toggles from the current consent state.</summary>
        public void Show()
        {
            if (_panel == null) return;

            // Showing the prompt only flips a display style — the GameObject itself never disables, so
            // LazyFollowAnchor's OnEnable re-anchor does not fire on the second and later shows. Without
            // this the panel reappears at whatever world yaw it was left at, which after a scene load is
            // wherever the previous scene's camera happened to be looking.
            if (_followAnchor != null) _followAnchor.Reanchor();

            // Previously-declined players see their choice reflected; first-timers default to ALLOW.
            if (_consent != null)
            {
                ConsentState state = _consent.GetConsentStatus();
                _micAllowed = state.MicConsent != ConsentStatus.Declined;
                _aiAllowed = state.InternetConsent != ConsentStatus.Declined;
            }

            _panel.style.display = DisplayStyle.Flex;
            PaintSegments();
        }

        private void Hide()
        {
            if (_panel != null) _panel.style.display = DisplayStyle.None;
        }

        private void HandleMicAllow() { _micAllowed = true; PaintSegments(); }
        private void HandleMicBlock() { _micAllowed = false; PaintSegments(); }
        private void HandleAiAllow() { _aiAllowed = true; PaintSegments(); }
        private void HandleAiBlock() { _aiAllowed = false; PaintSegments(); }

        /// <summary>Lights the chosen half of each row. Both halves are always live.</summary>
        private void PaintSegments()
        {
            _micAllow?.EnableInClassList(SelectedSegmentClass, _micAllowed);
            _micBlock?.EnableInClassList(SelectedSegmentClass, !_micAllowed);
            _aiAllow?.EnableInClassList(SelectedSegmentClass, _aiAllowed);
            _aiBlock?.EnableInClassList(SelectedSegmentClass, !_aiAllowed);
        }

        /// <summary>
        /// Submits whatever is on screen. CONFIRM is never disabled now: with an explicit BLOCK the
        /// panel always shows a complete answer, and a greyed-out Confirm on a consent screen reads
        /// as the game refusing to accept "no".
        ///
        /// Voice still needs BOTH — <see cref="IConsentService.IsVoiceModeAvailable"/> decides that,
        /// and blocking either simply means the merchants use the button sale.
        /// </summary>
        private void HandleConfirm()
        {
            Submit(_micAllowed ? ConsentStatus.Granted : ConsentStatus.Declined,
                   _aiAllowed ? ConsentStatus.Granted : ConsentStatus.Declined);
        }

        private void HandleDeclineAll()
        {
            Submit(ConsentStatus.Declined, ConsentStatus.Declined);
        }

        private void Submit(ConsentStatus mic, ConsentStatus internet)
        {
            Hide();

            if (_consent == null)
            {
                Debug.LogWarning("ConsentPromptUI: consent chosen but no IConsentService to receive it — " +
                                 "the player's answer has been dropped.", this);
                return;
            }

            _consent.SubmitConsentResult(mic, internet);
            Debug.Log($"ConsentPromptUI: player chose mic={mic}, internet={internet}.");
        }
    }
}
