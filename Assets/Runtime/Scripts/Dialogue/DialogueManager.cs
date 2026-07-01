using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

namespace DumyatRuntime
{
    public class DialogueManager : MonoBehaviour
    {
        public static DialogueManager Instance { get; private set; }

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

            foreach (SO_DialogueLine line in sequence.DialogueLines)
            {
                string textToShow = _settingsManager.GetLanguage() ? line.ArabicText : line.EnglishText;
                _floatingText = FloatingUIManager.Instance.Show(speaker.position, textToShow);

                _audioSource.clip = line.VoiceClip;
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
            }

            if (sequence.Type == DialogueType.Interrupting)
            {
                SetLocomotionEnabled(true);
            }

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