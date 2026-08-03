using System;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// The "are you sure?" overlay shared by the main menu and the pause menu. A plain C# wrapper
    /// over elements that already exist in the UXML — the same shape as
    /// <see cref="CraftingDiagnosticItemUI"/> — rather than a MonoBehaviour, because there is no
    /// GameObject to hang it on and nothing here needs a Unity lifecycle.
    ///
    /// One overlay serves every prompt: <see cref="Ask"/> rewrites the text and rebinds the
    /// confirm action, so adding a new destructive button costs a call, not another panel.
    /// </summary>
    public sealed class MenuConfirmOverlay
    {
        private readonly VisualElement _root;
        private readonly Label _title;
        private readonly Label _message;

        // Held so each Ask can replace the previous binding — without this, confirming would fire
        // every action ever asked for.
        private Action _onConfirm;

        public bool IsOpen => _root != null && _root.style.display == DisplayStyle.Flex;

        /// <param name="panelRoot">Root of the document containing ConfirmOverlay and its children.</param>
        public MenuConfirmOverlay(VisualElement panelRoot)
        {
            if (panelRoot == null) return;

            _root = panelRoot.Q<VisualElement>("ConfirmOverlay");
            if (_root == null) return;

            _title = _root.Q<Label>("ConfirmTitle");
            _message = _root.Q<Label>("ConfirmMessage");

            Button yes = _root.Q<Button>("ConfirmYesButton");
            if (yes != null)
            {
                yes.clicked += () =>
                {
                    Action confirmed = _onConfirm;
                    Hide();                 // hide first: the action usually starts a scene load
                    confirmed?.Invoke();
                };
            }

            Button no = _root.Q<Button>("ConfirmNoButton");
            if (no != null) no.clicked += Hide;

            Hide();
        }

        /// <summary>Shows the overlay. <paramref name="onConfirm"/> replaces any previous action.</summary>
        public void Ask(string title, string message, Action onConfirm)
        {
            if (_root == null) return;

            _onConfirm = onConfirm;

            if (_title != null) _title.text = title;
            if (_message != null) _message.text = message;

            _root.style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            _onConfirm = null;
            if (_root != null) _root.style.display = DisplayStyle.None;
        }
    }
}
