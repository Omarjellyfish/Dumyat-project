using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables; // Required for XRI v3.0+
using Reflex.Attributes;

namespace DumyatRuntime
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class MenuLocation : MonoBehaviour, IInteractable
    {
        [Header("Location Settings")]
        [SerializeField] private string _sceneName;
        [SerializeField] private string _unlockKey;

        [Header("References")]
        [SerializeField] private MenuManager _menuManager;

        private XRSimpleInteractable _interactable;
        private IPlayerPersistenceService _persistence;

        [Inject]
        private void Inject(IPlayerPersistenceService persistence) => _persistence = persistence;

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
            if (_persistence == null || !_persistence.HasKey(_unlockKey))
            {
                _menuManager.ShowLockedFeedback(this);
                return;
            }

            _menuManager.LoadLocation(_sceneName);
        }
    }
}
