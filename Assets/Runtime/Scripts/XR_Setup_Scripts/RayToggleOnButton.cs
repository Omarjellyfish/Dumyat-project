using UnityEngine;
using UnityEngine.InputSystem;

namespace DumyatRuntime
{
    public class RayToggleOnButton : MonoBehaviour
    {
        [Header("Ray Settings")]
        [SerializeField] private GameObject _lineVisualObject;

        [Header("Input")]
        [SerializeField] private InputActionReference _toggleAction; 

        private bool _isRayActive = false;

        private void OnEnable()
        {
            _toggleAction.action.performed += HandleButtonPressed;
            _toggleAction.action.Enable();
        }

        private void OnDisable()
        {
            _toggleAction.action.performed -= HandleButtonPressed;
        }

        private void Start()
        {
            HideRay();
        }

        private void HandleButtonPressed(InputAction.CallbackContext _ctx)
        {
            if (_isRayActive)
                HideRay();
            else
                ShowRay();
        }

        public void ShowRay()
        {
            _isRayActive = true;
            _lineVisualObject.SetActive(true);
        }

        public void HideRay()
        {
            _isRayActive = false;
            _lineVisualObject.SetActive(false);
        }
    }
}