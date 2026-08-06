using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    // Put this on the workbench, alongside the nails. It listens for
    // MenuManager's panel events and swaps visibility: when a panel opens,
    // the main menu nails (Start/Settings/Credits/Quit) hide and the
    // matching "back" nail appears; when the panel closes, menu nails come
    // back and reset to their un-hit starting position.
    public class NailGroupController : MonoBehaviour
    {
        [Header("Main menu nails")]
        [SerializeField] private GameObject[] _menuNails;

        [Header("Back nails")]
        [Tooltip("A nail with NailMenuAction set to CloseSettings.")]
        [SerializeField] private GameObject _settingsBackNail;
        [Tooltip("A nail with NailMenuAction set to CloseCredits.")]
        [SerializeField] private GameObject _creditsBackNail;

        private IEventService _eventService;

        [Inject]
        private void Inject(IEventService eventService)
        {
            _eventService = eventService;
        }

        private void OnEnable()
        {
            _eventService?.Subscribe<SettingsPanelToggledEvent>(OnSettingsToggled);
            _eventService?.Subscribe<CreditsPanelToggledEvent>(OnCreditsToggled);
        }

        private void OnDisable()
        {
            _eventService?.Unsubscribe<SettingsPanelToggledEvent>(OnSettingsToggled);
            _eventService?.Unsubscribe<CreditsPanelToggledEvent>(OnCreditsToggled);
        }

        private void OnSettingsToggled(SettingsPanelToggledEvent settingsEvent)
        {
            SetMenuNailsVisible(!settingsEvent.IsOpen);
            SetBackNailVisible(_settingsBackNail, settingsEvent.IsOpen);

            if (!settingsEvent.IsOpen)
                ResetAllMenuNails();
        }

        private void OnCreditsToggled(CreditsPanelToggledEvent creditsEvent)
        {
            SetMenuNailsVisible(!creditsEvent.IsOpen);
            SetBackNailVisible(_creditsBackNail, creditsEvent.IsOpen);

            if (!creditsEvent.IsOpen)
                ResetAllMenuNails();
        }

        private void SetMenuNailsVisible(bool isVisible)
        {
            if (_menuNails == null) return;

            foreach (GameObject nail in _menuNails)
            {
                if (nail != null)
                    nail.SetActive(isVisible);
            }
        }

        private void SetBackNailVisible(GameObject backNail, bool isVisible)
        {
            if (backNail == null) return;

            backNail.SetActive(isVisible);

            // Always start fresh so a previous hit doesn't carry over next time it appears.
            if (isVisible)
                backNail.GetComponent<NailAction>()?.ResetNail();
        }

        private void ResetAllMenuNails()
        {
            if (_menuNails == null) return;

            foreach (GameObject nail in _menuNails)
            {
                if (nail == null) continue;
                nail.GetComponent<NailAction>()?.ResetNail();
            }
        }
    }
}