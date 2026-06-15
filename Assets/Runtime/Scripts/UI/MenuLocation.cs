using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables; // Required for XRI v3.0+

namespace DumyatRuntime
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class MenuLocation : MonoBehaviour ,IInteractable
    {
    [Header("Location Settings")]
    [SerializeField] private string _sceneName;
    [SerializeField] private bool _isUnlocked = true;

    [Header("References")]
    [SerializeField] private MenuManager _menuManager;

    private XRSimpleInteractable _interactable;

    private void Awake()
    {
        _interactable = GetComponent<XRSimpleInteractable>();
    }

    private void OnEnable()
    {
        _interactable.selectEntered.AddListener(HandleInteraction);
    }

    private void OnDisable()
    {
        _interactable.selectEntered.RemoveListener(HandleInteraction);
    }

    private void HandleInteraction(SelectEnterEventArgs _args)
    {
        OnInteract();
    }

public void OnInteract()
{
    if (!_isUnlocked) 
    { 
        _menuManager.ShowLockedFeedback(this); 
        return; 
    }

    if (string.IsNullOrEmpty(_sceneName))
    {
        //Debug.LogWarning($"{gameObject.name}: No scene name assigned.");
        return;
    }

    _menuManager.LoadLocation(_sceneName);
}
    }
}
