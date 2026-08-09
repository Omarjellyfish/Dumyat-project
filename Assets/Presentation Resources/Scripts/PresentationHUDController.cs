using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class PresentationHUDController : MonoBehaviour
{
    [SerializeField] private PresentationManager _presentationManager;
    [SerializeField] private string _customActionId = "PlayVideo";
    
    private UIDocument _uiDocument;
    private Button _previousButton;
    private Button _nextButton;
    private Button _actionButton;

    private void Awake()
    {
        _uiDocument = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        var root = _uiDocument.rootVisualElement;
        if (root == null) return;

        _previousButton = root.Q<Button>("previous-button");
        _nextButton = root.Q<Button>("next-button");
        _actionButton = root.Q<Button>("action-button");

        if (_previousButton != null)
        {
            _previousButton.clicked += OnPreviousClicked;
        }

        if (_nextButton != null)
        {
            _nextButton.clicked += OnNextClicked;
        }

        if (_actionButton != null)
        {
            _actionButton.clicked += OnActionClicked;
        }
    }

    private void OnDisable()
    {
        if (_previousButton != null)
        {
            _previousButton.clicked -= OnPreviousClicked;
        }

        if (_nextButton != null)
        {
            _nextButton.clicked -= OnNextClicked;
        }

        if (_actionButton != null)
        {
            _actionButton.clicked -= OnActionClicked;
        }
    }

    private void OnPreviousClicked()
    {
        if (_presentationManager != null)
        {
            _presentationManager.PreviousLocation();
        }
        else
        {
            Debug.LogWarning("PresentationHUDController: PresentationManager is not assigned.");
        }
    }

    private void OnNextClicked()
    {
        if (_presentationManager != null)
        {
            _presentationManager.NextLocation();
        }
        else
        {
            Debug.LogWarning("PresentationHUDController: PresentationManager is not assigned.");
        }
    }

    private void OnActionClicked()
    {
        if (_presentationManager != null)
        {
            _presentationManager.TriggerCustomAction(_customActionId);
        }
        else
        {
            Debug.LogWarning("PresentationHUDController: PresentationManager is not assigned.");
        }
    }
}
