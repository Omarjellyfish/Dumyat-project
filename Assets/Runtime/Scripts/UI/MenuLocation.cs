using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables; // Required for XRI v3.0+

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
            if (!PlayerPersistence.Singleton.HasKey(_unlockKey))
            {
                _menuManager.ShowLockedFeedback(this);
                return;
            }

            _menuManager.LoadLocation(_sceneName);
        }
    }
}
