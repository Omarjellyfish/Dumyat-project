using System.Collections;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DumyatRuntime
{
    public class DialogueManager : MonoBehaviour, IDialogueService
    {
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private InputActionReference _buttonToSkip;
        [SerializeField] private SettingsManager _settingsManager;
        [SerializeField] private MonoBehaviour[] _locomotionComponentsToDisable;

        private IEventService _eventService;
        private IFloatingTextService _floatingTextService;

        private FloatingText _floatingText;
        private bool _currentlyPlaying;

        // Choice-wait state: set by SelectChoice(), which is called by whatever
        // Choice UI subscribes to DialogueChoicesPresentedEvent.
        private DialogueChoice _pickedChoice;
        private bool _choicePending;

        public bool IsPlaying => _currentlyPlaying;

        [Inject]
        private void Inject(IEventService eventService, IFloatingTextService floatingTextService)
        {
            _eventService = eventService;
            _floatingTextService = floatingTextService;
        }

        public void PlayDialogue(SO_DialogueSequence sequence, Transform speaker)
        {
            if (sequence == null || speaker == null) return;
            if (_currentlyPlaying) return;

            if (sequence.Type == DialogueType.Interrupting)
                SetLocomotionEnabled(false);

            StartCoroutine(PlaySequence(sequence, speaker));
        }

        public void SelectChoice(DialogueChoice choice)
        {
            if (!_choicePending) return;
            _pickedChoice = choice;
            _choicePending = false;
        }

        private IEnumerator PlaySequence(SO_DialogueSequence sequence, Transform speaker)
        {
            _currentlyPlaying = true;
            _eventService?.Publish(new DialogueStartedEvent(sequence));

            SO_DialogueLine current = sequence.StartingLine;

            while (current != null)
            {
                bool isArabic = _settingsManager != null && _settingsManager.IsArabic;
                string textToShow = isArabic ? current.ArabicText : current.EnglishText;

                _floatingText = _floatingTextService.Show(speaker.position, textToShow);
                _eventService?.Publish(new DialogueLineShownEvent(current, textToShow));

                _audioSource.clip = current.VoiceClip;
                _audioSource.Play();

                while (_audioSource.isPlaying)
                {
                    if (_buttonToSkip != null && _buttonToSkip.action.WasPressedThisFrame())
                    {
                        _audioSource.Stop();
                        break;
                    }
                    yield return null;
                }

                _floatingTextService.Hide(_floatingText);
                _floatingText = null;

                if (current.HasChoices)
                {
                    _pickedChoice = null;
                    _choicePending = true;

                    _eventService?.Publish(new DialogueChoicesPresentedEvent(current.Choices, SelectChoice));

                    // Waits here until some Choice UI calls SelectChoice(...) in
                    // response to the event above. No UI exists yet - this just
                    // makes DialogueManager ready for one without further changes.
                    while (_choicePending)
                        yield return null;

                    current = _pickedChoice != null ? _pickedChoice.NextLine : null;
                }
                else
                {
                    current = current.NextLine;
                }
            }

            if (sequence.Type == DialogueType.Interrupting)
                SetLocomotionEnabled(true);

            _eventService?.Publish(new DialogueFinishedEvent(sequence));
            _currentlyPlaying = false;
        }

        private void SetLocomotionEnabled(bool isEnabled)
        {
            foreach (MonoBehaviour component in _locomotionComponentsToDisable)
            {
                if (component != null)
                    component.enabled = isEnabled;
            }
        }
    }
}
