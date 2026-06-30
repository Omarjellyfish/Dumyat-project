using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using System.Collections;
namespace DumyatRuntime
{
    public class DialogueManager : MonoBehaviour
    {
        public static DialogueManager Instance { get; private set; }
        private FloatingText _floatingText;
        [SerializeField] AudioSource _audioSource;
        [SerializeField] InputActionReference _buttonToSkip;
        private bool _currentlyPlay;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void PlayDialogue(SO_DialogueSequence sequences, Transform speaker)
        {
            if (_currentlyPlay) return;
            if (sequences.Type == DialogueType.Interrupting)
            {
                StartCoroutine(PlaySequence(sequences, speaker));
            }
            else
            {
                StartCoroutine(PlaySequence(sequences, speaker));
            }
        }

        IEnumerator PlaySequence(SO_DialogueSequence sequence, Transform speaker)
        {
            _currentlyPlay = true;

            foreach (SO_DialogueLine line in sequence.DialogueLines)
            {
                if (sequence._arabic)
                {
                    FloatingUIManager.Instance.Show(speaker.position, line.ArabicText);
                }
                else
                    FloatingUIManager.Instance.Show(speaker.position, line.EnglishText);

                _audioSource.clip = line.VoiceClip;
                _audioSource.Play();
                while (_audioSource.isPlaying && _buttonToSkip.action.WasPressedThisFrame())
                {
                yield return null;
                    
                }
                if(!_buttonToSkip.action.WasPressedThisFrame())
                {
                    _audioSource.Stop();
                    FloatingUIManager.Instance.Hide(_floatingText);
                }
            }
        }
        
    }
}
