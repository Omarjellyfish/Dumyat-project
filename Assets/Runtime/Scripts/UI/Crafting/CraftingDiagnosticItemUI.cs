using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Controller for a single diagnostic line item in the UI Toolkit hierarchy.
    /// Does not need to inherit from MonoBehaviour since it wraps a dynamically instantiated VisualElement.
    /// </summary>
    public class CraftingDiagnosticItemUI
    {
        private VisualElement _iconElement;
        private Label _messageLabel;

        public CraftingDiagnosticItemUI(VisualElement root)
        {
            _iconElement = root.Q<VisualElement>("item-icon");
            _messageLabel = root.Q<Label>("item-text");
        }

        public void Setup(string message, Color color, Texture2D iconTexture)
        {
            if (_messageLabel != null)
            {
                _messageLabel.text = message;
                _messageLabel.style.color = color;
            }

            if (_iconElement != null)
            {
                if (iconTexture != null)
                {
                    _iconElement.style.backgroundImage = iconTexture;
                }
                _iconElement.style.unityBackgroundImageTintColor = color;
            }
        }
    }
}
