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

        IEnumerator PlaySequence(SO_DialogueSequence sequences, Transform speaker)
        {
            _currentlyPlay = true;
            foreach(SO_DialogueSequence sequence in sequences._dialogueLines)
            {
                
            }
        }
    }
}
