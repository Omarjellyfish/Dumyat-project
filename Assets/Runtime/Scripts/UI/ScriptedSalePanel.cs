using System;
using System.Collections.Generic;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UI;

namespace DumyatRuntime
{
    /// <summary>
    /// World-space button panel for the SCRIPTED (offline) merchant sale. Sits on (or under) a merchant next
    /// to its subtitle panel and shows only the buttons the sale currently offers, driven by
    /// <see cref="ScriptedChoiceRequestedEvent"/>. A click publishes <see cref="ScriptedChoiceMadeEvent"/>,
    /// which <see cref="ScriptedSaleController"/> reads as the player's decision — the buttons ARE the
    /// intent, so the scripted flow never parses text (see CLAUDE.md's no-keyword-intent rule).
    ///
    /// Mirrors <see cref="MerchantSubtitlePresenter"/>: it is the only piece that knows about both the
    /// <see cref="MerchantNPC"/> and the UI, so the buttons stay reusable and the merchant stays UI-free.
    /// Requires the buttons to be ray-clickable in VR (a Canvas with a TrackedDeviceGraphicRaycaster, as in
    /// the crafting validation UI); this component only shows/hides them and wires their clicks.
    /// </summary>
    public class ScriptedSalePanel : MonoBehaviour
    {
        /// <summary>One button mapped to the choice it makes.</summary>
        [Serializable]
        public struct ChoiceButton
        {
            [Tooltip("Which scripted choice this button sends when clicked.")]
            public ScriptedChoice choice;

            [Tooltip("The uGUI button. Its GameObject is shown only while this choice is offered.")]
            public Button button;
        }

        [Tooltip("The merchant whose scripted sale this serves. Auto-resolved from a parent if empty.")]
        [SerializeField] private MerchantNPC _merchant;

        [Tooltip("Optional container toggled off whenever NO buttons are showing, so the whole panel " +
                 "(background, etc.) hides between prompts. Leave empty to just toggle the buttons.")]
        [SerializeField] private GameObject _visualRoot;

        [Tooltip("Every button this panel can present, each mapped to its choice. Confirm placement, " +
                 "Accept and Decline are the three the scripted sale uses.")]
        [SerializeField] private ChoiceButton[] _buttons = Array.Empty<ChoiceButton>();

        private IEventService _events;

        // Cached click delegates keyed by button, so we can cleanly remove them in OnDisable.
        private readonly Dictionary<Button, UnityEngine.Events.UnityAction> _handlers =
            new Dictionary<Button, UnityEngine.Events.UnityAction>();

        [Inject]
        private void Inject(IEventService events) => _events = events;

        private void Awake()
        {
            if (_merchant == null) _merchant = GetComponentInParent<MerchantNPC>();
            if (_merchant == null)
                Debug.LogWarning($"{nameof(ScriptedSalePanel)} on '{name}' has no MerchantNPC in its " +
                                 "parents — it will not show any buttons.", this);
            HideAll();
        }

        private void OnEnable()
        {
            // Wire each button to publish its choice. Cache the delegate so OnDisable removes exactly it.
            foreach (ChoiceButton entry in _buttons)
            {
                if (entry.button == null) continue;
                ScriptedChoice choice = entry.choice;
                UnityEngine.Events.UnityAction handler = () => OnButtonClicked(choice);
                _handlers[entry.button] = handler;
                entry.button.onClick.AddListener(handler);
            }

            if (_events == null)
            {
                Debug.LogWarning($"{nameof(ScriptedSalePanel)} on '{name}' has no IEventService injected " +
                                 "— buttons will not appear. Does this scene have a ContainerScope?", this);
                return;
            }
            _events.Subscribe<ScriptedChoiceRequestedEvent>(HandleChoiceRequested);
            _events.Subscribe<MerchantStoppedSpeakingEvent>(HandleStopped);
        }

        private void OnDisable()
        {
            foreach (KeyValuePair<Button, UnityEngine.Events.UnityAction> pair in _handlers)
                if (pair.Key != null) pair.Key.onClick.RemoveListener(pair.Value);
            _handlers.Clear();

            if (_events == null) return;
            _events.Unsubscribe<ScriptedChoiceRequestedEvent>(HandleChoiceRequested);
            _events.Unsubscribe<MerchantStoppedSpeakingEvent>(HandleStopped);
        }

        private void OnButtonClicked(ScriptedChoice choice)
        {
            if (_events == null || _merchant == null) return;
            _events.Publish(new ScriptedChoiceMadeEvent(_merchant, choice));
        }

        // Every merchant shares the channel — react only to our own merchant's requests.
        private void HandleChoiceRequested(ScriptedChoiceRequestedEvent evt)
        {
            if (evt.Merchant != _merchant) return;
            ShowOnly(evt.Options);
        }

        // The scripted sale tearing down clears any lingering buttons.
        private void HandleStopped(MerchantStoppedSpeakingEvent evt)
        {
            if (evt.Merchant != _merchant) return;
            HideAll();
        }

        private void ShowOnly(ScriptedChoice[] options)
        {
            bool any = false;
            foreach (ChoiceButton entry in _buttons)
            {
                if (entry.button == null) continue;
                bool show = options != null && Array.IndexOf(options, entry.choice) >= 0;
                if (entry.button.gameObject.activeSelf != show) entry.button.gameObject.SetActive(show);
                any |= show;
            }
            if (_visualRoot != null && _visualRoot.activeSelf != any) _visualRoot.SetActive(any);
        }

        private void HideAll() => ShowOnly(Array.Empty<ScriptedChoice>());
    }
}
