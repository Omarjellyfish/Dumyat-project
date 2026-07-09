using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public interface IDialogueService
    {
        bool IsPlaying { get; }
        void PlayDialogue(SO_DialogueSequence sequence, Transform speaker);
    }

    public readonly struct DialogueStartedEvent : IGameEvent
    {
        public readonly SO_DialogueSequence Sequence;
        public readonly Transform Speaker;

        public DialogueStartedEvent(SO_DialogueSequence sequence, Transform speaker)
        {
            Sequence = sequence;
            Speaker = speaker;
        }
    }

    public readonly struct DialogueLineStartedEvent : IGameEvent
    {
        public readonly SO_DialogueSequence Sequence;
        public readonly SO_DialogueLine Line;
        public readonly Transform Speaker;

        public DialogueLineStartedEvent(SO_DialogueSequence sequence, SO_DialogueLine line, Transform speaker)
        {
            Sequence = sequence;
            Line = line;
            Speaker = speaker;
        }
    }

    public readonly struct DialogueFinishedEvent : IGameEvent
    {
        public readonly SO_DialogueSequence Sequence;

        public DialogueFinishedEvent(SO_DialogueSequence sequence)
        {
            Sequence = sequence;
        }
    }

    public class DialogueManager : MonoBehaviour, IDialogueService
    {
        public static event System.Action<SO_DialogueSequence> OnDialogueFinished;

        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private InputActionReference _buttonToSkip;
        [SerializeField] private SettingsManager _settingsManager;
        [SerializeField] private MonoBehaviour[] _locomotionComponentsToDisable;

        private IFloatingTextService _floatingTextService;
        private IEventService _eventService;
        private FloatingText _floatingText;
        private bool _currentlyPlay;

        public bool IsPlaying => _currentlyPlay;

        [Inject]
        private void Inject(IFloatingTextService floatingTextService, IEventService eventService)
        {
            _floatingTextService = floatingTextService;
            _eventService = eventService;
        }

        public void PlayDialogue(SO_DialogueSequence sequence, Transform speaker)
        {
            if (_currentlyPlay || sequence == null || speaker == null)
                return;

            if (sequence.Type == DialogueType.Interrupting)
            {
                SetLocomotionEnabled(false);
            }

            StartCoroutine(PlaySequence(sequence, speaker));
        }

        private IEnumerator PlaySequence(SO_DialogueSequence sequence, Transform speaker)
        {
            _currentlyPlay = true;
            _eventService?.Publish(new DialogueStartedEvent(sequence, speaker));

            SO_DialogueLine current = sequence.StartingLine;

            while (current != null)
            {
                string textToShow = _settingsManager.GetLanguage() ? current.ArabicText : current.EnglishText;
                _eventService?.Publish(new DialogueLineStartedEvent(sequence, current, speaker));
                _floatingText = _floatingTextService?.Show(speaker.position, textToShow);

                _audioSource.clip = current.VoiceClip;
                _audioSource.Play();

                while (_audioSource.isPlaying)
                {
                    if (_buttonToSkip.action.WasPressedThisFrame())
                    {
                        _audioSource.Stop();
                        break;
                    }
                    yield return null;
                }

                _floatingTextService?.Hide(_floatingText);

                if (current.HasChoices)
                {
                    // TODO: show choice UI, wait for player pick
                    // current = pickedChoice.NextLine;
                    current = null; // placeholder until choice UI exists
                }
                else
                {
                    current = current.NextLine;
                }
            }

            if (sequence.Type == DialogueType.Interrupting)
            {
                SetLocomotionEnabled(true);
            }

            OnDialogueFinished?.Invoke(sequence);
            _eventService?.Publish(new DialogueFinishedEvent(sequence));
            _currentlyPlay = false;
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
