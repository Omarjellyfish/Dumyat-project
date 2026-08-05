using UnityEngine;
using TMPro;

namespace DumyatRuntime
{
    public class FloatingText : MonoBehaviour
    {
        [SerializeField] private TextMeshPro _textMesh;

        private readonly CameraBillboard _billboard = new CameraBillboard();

        private void Awake()
        {
            if (_textMesh == null)
                _textMesh = GetComponentInChildren<TextMeshPro>();
        }

        private void LateUpdate()
        {
            // Upright, so the popup stops rolling with the player's head — the old inline billboard
            // passed no up-vector, which tilted the text whenever they leaned.
            _billboard.Apply(transform);
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