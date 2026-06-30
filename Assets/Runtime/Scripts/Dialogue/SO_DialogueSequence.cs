using System;
using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "SO_DialogueSequence", menuName = "Scriptable Objects/SO_DialogueSequence")]
    public class SO_DialogueSequence : ScriptableObject
    {
        [SerializeField] private List<SO_DialogueLine> _dialogueLines;
        [SerializeField] private DialogueType _dialogueType;
        [SerializeField] private bool _choosen;
        public List<SO_DialogueLine> DialogueLines => _dialogueLines;
        public DialogueType Type => _dialogueType;
        public bool _arabic => _choosen;
    }
    public enum DialogueType
    {
        Parallel,
        Interrupting
    }
}


/*
Set _currentlyPlay = true

foreach (line in sequence.DialogueLines):
    - decide which text to use (Arabic or English) based on SettingsManager
    - call FloatingUIManager.Instance.Show(speaker.position, chosenText), store result in _floatingText
    - set _audioSource.clip = line.VoiceClip
    - call _audioSource.Play()
    - while (_audioSource.isPlaying AND skip button wasn't pressed this frame):
          yield return null
    - if skip was what stopped the loop: call _audioSource.Stop()
    - call FloatingUIManager.Instance.Hide(_floatingText)

Set _currentlyPlay = false
*/