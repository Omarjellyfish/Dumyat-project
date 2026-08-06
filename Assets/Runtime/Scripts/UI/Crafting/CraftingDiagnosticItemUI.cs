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
        private Label _codeLabel;

        public CraftingDiagnosticItemUI(VisualElement root)
        {
            _iconElement = root.Q<VisualElement>("item-icon");
            _messageLabel = root.Q<Label>("item-text");
            _codeLabel = root.Q<Label>("item-code");
        }

        /// <summary>
        /// Fills the row. <paramref name="reasonCode"/> is the short failure kind ("OVER", "ABSENT")
        /// shown in its own outlined cell; pass null or empty to hide the cell.
        /// </summary>
        public void Setup(string message, Color color, Texture2D iconTexture, string reasonCode = null)
        {
            if (_messageLabel != null)
            {
                _messageLabel.text = message;
                _messageLabel.style.color = color;
            }

            if (_codeLabel != null)
            {
                bool hasCode = !string.IsNullOrEmpty(reasonCode);
                _codeLabel.style.display = hasCode ? DisplayStyle.Flex : DisplayStyle.None;

                if (hasCode)
                {
                    _codeLabel.text = reasonCode;
                    _codeLabel.style.color = color;
                    _codeLabel.style.borderTopColor = color;
                    _codeLabel.style.borderRightColor = color;
                    _codeLabel.style.borderBottomColor = color;
                    _codeLabel.style.borderLeftColor = color;
                }
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
