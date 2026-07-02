using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

namespace DumyatRuntime
{
    public class DialogueManager : MonoBehaviour
    {
        public static DialogueManager Instance { get; private set; }
        public static event System.Action<SO_DialogueSequence> OnDialogueFinished;

        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private InputActionReference _buttonToSkip;
        [SerializeField] private SettingsManager _settingsManager;
        [SerializeField] private MonoBehaviour[] _locomotionComponentsToDisable;

        private FloatingText _floatingText;
        private bool _currentlyPlay;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void PlayDialogue(SO_DialogueSequence sequence, Transform speaker)
        {
            if (_currentlyPlay) return;

            if (sequence.Type == DialogueType.Interrupting)
            {
                SetLocomotionEnabled(false);
            }

            StartCoroutine(PlaySequence(sequence, speaker));
        }

        private IEnumerator PlaySequence(SO_DialogueSequence sequence, Transform speaker)
        {
            _currentlyPlay = true;

            SO_DialogueLine current = sequence.StartingLine;

            while (current != null)
            {
                string textToShow = _settingsManager.GetLanguage() ? current.ArabicText : current.EnglishText;
                _floatingText = FloatingUIManager.Instance.Show(speaker.position, textToShow);

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

                FloatingUIManager.Instance.Hide(_floatingText);

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