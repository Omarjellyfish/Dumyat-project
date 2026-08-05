using UnityEngine;
using TMPro;

namespace DumyatRuntime
{
    public class FloatingText : MonoBehaviour
    {
        [SerializeField] private TextMeshPro _textMesh;
        private Camera _mainCamera;

        private void Awake()
        {
            if (_textMesh == null)
                _textMesh = GetComponentInChildren<TextMeshPro>();
        }

        private void OnEnable()
        {
            _mainCamera = Camera.main;
        }

        private void LateUpdate()
        {
            if (_mainCamera == null) return;

            transform.rotation = Quaternion.LookRotation(
                transform.position - _mainCamera.transform.position);
        }

        public void SetText(string text)
        {
            _textMesh.text = text;
        }

        public void SetPosition(Vector3 worldPosition , Vector3 offset)
        {
            transform.position = worldPosition + offset;
        }

        public void Show()
        {
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}