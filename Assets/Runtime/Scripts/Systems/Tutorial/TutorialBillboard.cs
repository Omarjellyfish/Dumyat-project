using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Y-axis billboard that faces the XR camera. Placed on the tutorial hover UI
    /// so the panel always faces the player without tilting vertically.
    /// </summary>
    public class TutorialBillboard : MonoBehaviour
    {
        private Transform _cameraTransform;

        private void Awake()
        {
            _cameraTransform = Camera.main != null ? Camera.main.transform : null;
        }

        private void LateUpdate()
        {
            if (_cameraTransform == null)
            {
                // Retry — camera may spawn after this component
                if (Camera.main != null)
                {
                    _cameraTransform = Camera.main.transform;
                }
                return;
            }

            // Y-axis only rotation — keeps the panel upright
            Vector3 lookDirection = _cameraTransform.position - transform.position;
            lookDirection.y = 0f;

            if (lookDirection.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(-lookDirection.normalized, Vector3.up);
            }
        }
    }
}
