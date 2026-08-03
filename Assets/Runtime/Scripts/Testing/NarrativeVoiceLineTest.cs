using System.Collections;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime.Testing
{
    /// <summary>
    /// Live voice line test for the Narrative Engine.
    /// Plays actual audio clips through your SoundManager and shows subtitles in the console.
    /// Waits for each voice line to finish before auto-advancing to the next.
    ///
    /// Setup:
    ///   1. Create an empty GameObject → Add this component.
    ///   2. In Inspector, assign CharacterDialogueDataSO assets that have voice lines.
    ///   3. Assign DialogueSequenceSO assets to the test slots.
    ///   4. Enter Play Mode → Right-click component → "Play Main Sequence" (or use buttons).
    ///
    /// The script listens to NarrativeService events, plays the AudioClip via ISoundService,
    /// and auto-advances after the clip duration. You can also manually advance/choose.
    /// </summary>
    public class NarrativeVoiceLineTest : MonoBehaviour
    {
        [Header("Sequences to Test")]
        [Tooltip("The main dialogue sequence to test")]
        [SerializeField] private DialogueSequenceSO _mainSequence;

        [Tooltip("Optional: A high-priority sequence to interrupt with mid-dialogue")]
        [SerializeField] private DialogueSequenceSO _interruptSequence;

        [Header("Audio Settings")]
        [Tooltip("Volume for voice lines (0-1)")]
        [SerializeField] private float _voiceVolume = 1f;

        [Tooltip("If true, automatically advance to the next line after the voice clip finishes")]
        [SerializeField] private bool _autoAdvance = true;

        [Tooltip("Extra delay after a voice clip finishes before auto-advancing (seconds)")]
        [SerializeField] private float _autoAdvanceDelay = 0.3f;

        [Inject] private readonly INarrativeService _narrativeService;
        [Inject] private readonly IEventService _eventService;
        [Inject] private readonly ISoundService _soundService;

        private Coroutine _autoAdvanceCoroutine;
        private bool _isWaitingForChoice;

        private void OnEnable()
        {
            _eventService.Subscribe<DialogueLineStartedEvent>(OnLineStarted);
            _eventService.Subscribe<DialogueChoicesEvent>(OnChoicesPresented);
            _eventService.Subscribe<DialogueChoiceDismissedEvent>(OnChoicesDismissed);
            _eventService.Subscribe<DialogueEndedEvent>(OnDialogueEnded);
            _eventService.Subscribe<DialogueInterruptedEvent>(OnInterrupted);
            _eventService.Subscribe<DialogueResumedEvent>(OnResumed);
        }

        private void OnDisable()
        {
            _eventService.Unsubscribe<DialogueLineStartedEvent>(OnLineStarted);
            _eventService.Unsubscribe<DialogueChoicesEvent>(OnChoicesPresented);
            _eventService.Unsubscribe<DialogueChoiceDismissedEvent>(OnChoicesDismissed);
            _eventService.Unsubscribe<DialogueEndedEvent>(OnDialogueEnded);
            _eventService.Unsubscribe<DialogueInterruptedEvent>(OnInterrupted);
            _eventService.Unsubscribe<DialogueResumedEvent>(OnResumed);

            StopAutoAdvance();
        }

        // =====================================================================
        // Controls — Context Menu + Inspector buttons
        // =====================================================================

        [ContextMenu("Play Main Sequence")]
        public void PlayMainSequence()
        {
            if (_mainSequence == null)
            {
                Debug.LogWarning("[VoiceTest] No Main Sequence assigned in Inspector.");
                return;
            }
            LogVoice($"▶ Playing: {_mainSequence.SequenceId} (Priority: {_mainSequence.Priority})");
            _narrativeService.PlayDialogue(_mainSequence, _mainSequence.Priority);
        }

        [ContextMenu("Fire Interrupt Sequence")]
        public void FireInterrupt()
        {
            if (_interruptSequence == null)
            {
                Debug.LogWarning("[VoiceTest] No Interrupt Sequence assigned in Inspector.");
                return;
            }
            LogVoice($"⚡ Interrupting with: {_interruptSequence.SequenceId} (Priority: {_interruptSequence.Priority})");
            _narrativeService.PlayDialogue(_interruptSequence, _interruptSequence.Priority);
        }

        [ContextMenu("Advance Line (Manual)")]
        public void ManualAdvance()
        {
            StopAutoAdvance();
            _narrativeService.AdvanceLine();
        }

        [ContextMenu("Select Choice 0")]
        public void SelectChoice0() => _narrativeService.SelectChoice(0);

        [ContextMenu("Select Choice 1")]
        public void SelectChoice1() => _narrativeService.SelectChoice(1);

        [ContextMenu("Select Choice 2")]
        public void SelectChoice2() => _narrativeService.SelectChoice(2);

        [ContextMenu("Cancel Current Dialogue")]
        public void Cancel() => _narrativeService.CancelCurrent();

        // =====================================================================
        // Event Handlers
        // =====================================================================

        private void OnLineStarted(DialogueLineStartedEvent evt)
        {
            LogVoice($"🗣 [{evt.SpeakerName}]: \"{evt.SubtitleText}\"");

            // Play the voice clip through SoundManager
            if (evt.VoiceClip != null)
            {
                LogVoice($"   🔊 Playing clip: {evt.VoiceClip.name} ({evt.Duration:F2}s)");
                _soundService.PlayVoiceLine(evt.VoiceClip, transform, _voiceVolume);
            }
            else
            {
                LogVoice($"   📝 No voice clip — subtitle only ({evt.Duration:F2}s)");
            }

            // Auto-advance after duration
            if (_autoAdvance && !_isWaitingForChoice)
            {
                StopAutoAdvance();
                _autoAdvanceCoroutine = StartCoroutine(AutoAdvanceAfter(evt.Duration + _autoAdvanceDelay));
            }
        }

        private void OnChoicesPresented(DialogueChoicesEvent evt)
        {
            _isWaitingForChoice = true;
            StopAutoAdvance();

            LogVoice($"❓ {evt.PromptText}");
            for (int i = 0; i < evt.Choices.Count; i++)
            {
                LogVoice($"   [{i}] {evt.Choices[i].ChoiceText}");
            }
            LogVoice("   → Right-click this component → 'Select Choice 0/1/2' to choose");

            if (evt.TimeoutSeconds > 0)
            {
                LogVoice($"   ⏱ Timeout in {evt.TimeoutSeconds}s");
            }
        }

        private void OnChoicesDismissed(DialogueChoiceDismissedEvent evt)
        {
            _isWaitingForChoice = false;
        }

        private void OnDialogueEnded(DialogueEndedEvent evt)
        {
            StopAutoAdvance();
            LogVoice("✅ Dialogue ended.");
        }

        private void OnInterrupted(DialogueInterruptedEvent evt)
        {
            StopAutoAdvance();
            LogVoice("⚠ Current dialogue interrupted!");
        }

        private void OnResumed(DialogueResumedEvent evt)
        {
            LogVoice($"↩ Dialogue resumed — next speaker: {evt.SpeakerName}");
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private IEnumerator AutoAdvanceAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            _autoAdvanceCoroutine = null;

            if (_narrativeService.IsPlaying && !_isWaitingForChoice)
            {
                LogVoice("   ⏩ Auto-advancing...");
                _narrativeService.AdvanceLine();
            }
        }

        private void StopAutoAdvance()
        {
            if (_autoAdvanceCoroutine != null)
            {
                StopCoroutine(_autoAdvanceCoroutine);
                _autoAdvanceCoroutine = null;
            }
        }

        private static void LogVoice(string message)
        {
            Debug.Log($"<color=#FF9800>[VoiceTest]</color> {message}");
        }
    }
}
