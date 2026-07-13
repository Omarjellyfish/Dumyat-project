using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Stack-based narrative engine with interruption and resume support.
    /// Plain C# service constructed by Reflex. Subscribes to <see cref="PlayDialogueEvent"/>
    /// and publishes line/choice/end events for UI consumers.
    /// </summary>
    public sealed class NarrativeService : INarrativeService
    {
        private const int MAX_STACK_DEPTH = 3;
        private const float CHARS_PER_SECOND = 15f;
        private const float MIN_LINE_DURATION = 1f;
        private const int MAX_SKIP_ITERATIONS = 100;

        private class DialogueState
        {
            public DialogueSequenceSO Sequence;
            public int CurrentLineIndex;
            public bool IsWaitingForChoice;
            public int ChoicePointIndex;
            public List<DialogueChoice> FilteredChoices;
        }

        private class PendingDialogue
        {
            public DialogueSequenceSO Sequence;
            public DialoguePriority Priority;
        }

        private readonly IEventService _eventService;
        private readonly IPlayerPersistenceService _persistenceService;

        private readonly Stack<DialogueState> _stack = new();
        private readonly List<PendingDialogue> _overflowQueue = new();

        private DialogueState _current;

        public NarrativeService(IEventService eventService, IPlayerPersistenceService persistenceService)
        {
            _eventService = eventService;
            _persistenceService = persistenceService;

            _eventService.Subscribe<PlayDialogueEvent>(OnPlayDialogueEvent);
        }

        public bool IsPlaying => _current != null;

        public void PlayDialogue(DialogueSequenceSO sequence, DialoguePriority priority)
        {
            if (sequence == null) return;

            // Deduplication: prevent same sequence from playing if already active or queued
            string incomingId = sequence.SequenceId;
            if (_current != null && IsSameSequence(_current.Sequence, sequence, incomingId)) return;
            foreach (var state in _stack)
                if (IsSameSequence(state.Sequence, sequence, incomingId)) return;
            foreach (var pending in _overflowQueue)
                if (IsSameSequence(pending.Sequence, sequence, incomingId)) return;

            if (_current == null)
            {
                StartNewSequence(sequence);
            }
            else
            {
                // Handle interruption logic
                if (!_current.Sequence.CanBeInterrupted)
                {
                    QueueDialogue(sequence, priority);
                }
                else if (priority > _current.Sequence.Priority)
                {
                    if (_stack.Count >= MAX_STACK_DEPTH)
                    {
                        QueueDialogue(sequence, priority);
                    }
                    else
                    {
                        InterruptCurrent(sequence);
                    }
                }
                else
                {
                    QueueDialogue(sequence, priority);
                }
            }
        }

        public void AdvanceLine()
        {
            if (_current == null || _current.IsWaitingForChoice) return;

            _current.CurrentLineIndex = GetNextLineIndex(_current.CurrentLineIndex);

            PlayCurrentLineOrFinish();
        }

        public void SelectChoice(int choiceIndex)
        {
            if (_current == null || !_current.IsWaitingForChoice) return;
            if (_current.FilteredChoices == null) return;

            // Validate against the filtered list the UI actually received
            if (choiceIndex < 0 || choiceIndex >= _current.FilteredChoices.Count) return;

            _eventService.Publish(new DialogueChoiceDismissedEvent());

            var choice = _current.FilteredChoices[choiceIndex];
            _current.IsWaitingForChoice = false;
            _current.FilteredChoices = null;
            _current.CurrentLineIndex = choice.JumpToLineIndex;
            PlayCurrentLineOrFinish();
        }

        public void CancelCurrent()
        {
            if (_current == null) return;

            _eventService.Publish(new DialogueChoiceDismissedEvent());
            FinishCurrentDialogue();
        }

        // -------------------------------------------------------------------------
        // Internal flow
        // -------------------------------------------------------------------------

        private void OnPlayDialogueEvent(PlayDialogueEvent evt)
        {
            PlayDialogue(evt.Sequence, evt.Priority);
        }

        private void StartNewSequence(DialogueSequenceSO sequence)
        {
            _current = new DialogueState
            {
                Sequence = sequence,
                CurrentLineIndex = 0,
                IsWaitingForChoice = false
            };
            PlayCurrentLineOrFinish();
        }

        private void InterruptCurrent(DialogueSequenceSO newSequence)
        {
            _stack.Push(_current);
            _eventService.Publish(new DialogueChoiceDismissedEvent());
            _eventService.Publish(new DialogueInterruptedEvent());
            StartNewSequence(newSequence);
        }

        private void QueueDialogue(DialogueSequenceSO sequence, DialoguePriority priority)
        {
            _overflowQueue.Add(new PendingDialogue { Sequence = sequence, Priority = priority });
            // Sort queue by priority descending
            _overflowQueue.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        private void PlayCurrentLineOrFinish()
        {
            // Loop instead of recursion to avoid StackOverflow when skipping persistence-gated lines
            int safetyCounter = 0;
            while (safetyCounter < MAX_SKIP_ITERATIONS)
            {
                safetyCounter++;

                if (_current.CurrentLineIndex < 0 || _current.CurrentLineIndex >= _current.Sequence.Lines.Count)
                {
                    FinishCurrentDialogue();
                    return;
                }

                var line = _current.Sequence.Lines[_current.CurrentLineIndex];

                // Evaluate persistence conditions — skip if not met
                if (!string.IsNullOrEmpty(line.PersistenceConditionKey))
                {
                    bool hasKey = _persistenceService.HasKey(line.PersistenceConditionKey);
                    if (hasKey != line.RequiredState)
                    {
                        _current.CurrentLineIndex = GetNextLineIndex(_current.CurrentLineIndex);
                        continue; // Loop back instead of recursive AdvanceLine()
                    }
                }

                // Condition passed (or no condition) — play this line
                break;
            }

            if (safetyCounter >= MAX_SKIP_ITERATIONS)
            {
                Debug.LogWarning($"[NarrativeService] Skipped {MAX_SKIP_ITERATIONS} lines in sequence " +
                                 $"'{_current.Sequence.SequenceId}'. Possible misconfigured persistence conditions. Finishing dialogue.");
                FinishCurrentDialogue();
                return;
            }

            var currentLine = _current.Sequence.Lines[_current.CurrentLineIndex];

            // Set keys if required
            if (!string.IsNullOrEmpty(currentLine.PersistenceKeyToSet))
            {
                _persistenceService.AddKey(currentLine.PersistenceKeyToSet);
            }

            // Fire event for UI and Audio
            string speakerName = ResolveSpeakerName(currentLine);
            AudioClip clip = currentLine.Character != null ? currentLine.Character.GetClip(currentLine.VoiceLineKey) : null;
            string subtitleText = currentLine.SubtitleText ?? string.Empty;

            float duration = clip != null
                ? clip.length
                : Mathf.Max(MIN_LINE_DURATION, subtitleText.Length / CHARS_PER_SECOND);

            _eventService.Publish(new DialogueLineStartedEvent(speakerName, subtitleText, clip, duration));

            // Check if we need to present choices after this line
            CheckForChoicePoint();
        }

        private void CheckForChoicePoint()
        {
            for (int i = 0; i < _current.Sequence.ChoicePoints.Count; i++)
            {
                var choicePoint = _current.Sequence.ChoicePoints[i];
                if (choicePoint.AfterLineIndex == _current.CurrentLineIndex)
                {
                    // Filter choices by persistence conditions
                    var availableChoices = new List<DialogueChoice>();
                    foreach (var choice in choicePoint.Choices)
                    {
                        if (!string.IsNullOrEmpty(choice.RequiredPersistenceKey))
                        {
                            if (_persistenceService.HasKey(choice.RequiredPersistenceKey) != choice.RequiredState)
                                continue;
                        }
                        availableChoices.Add(choice);
                    }

                    _current.IsWaitingForChoice = true;
                    _current.ChoicePointIndex = i;
                    _current.FilteredChoices = availableChoices;

                    _eventService.Publish(new DialogueChoicesEvent(choicePoint.PromptText, availableChoices, choicePoint.TimeoutSeconds));
                    return;
                }
            }
        }

        private int GetNextLineIndex(int currentIndex)
        {
            if (currentIndex < 0 || currentIndex >= _current.Sequence.Lines.Count)
                return _current.Sequence.Lines.Count; // Out of bounds → will trigger finish

            var line = _current.Sequence.Lines[currentIndex];
            if (line.NextLineIndex >= 0) return line.NextLineIndex;
            return currentIndex + 1;
        }

        private void FinishCurrentDialogue()
        {
            _current = null;

            // Check queue for higher priority first
            if (_overflowQueue.Count > 0)
            {
                var nextPending = _overflowQueue[0];

                // If stack has something, only pull from queue if priority is higher than stacked
                if (_stack.Count > 0)
                {
                    var topOfStack = _stack.Peek();
                    if (nextPending.Priority > topOfStack.Sequence.Priority)
                    {
                        _overflowQueue.RemoveAt(0);
                        StartNewSequence(nextPending.Sequence);
                        return;
                    }
                }
                else
                {
                    _overflowQueue.RemoveAt(0);
                    StartNewSequence(nextPending.Sequence);
                    return;
                }
            }

            // Otherwise resume from stack
            if (_stack.Count > 0)
            {
                var resumedState = _stack.Pop();
                if (resumedState.Sequence.ResumeAfterInterruption)
                {
                    _current = resumedState;
                    string speakerName = "Unknown";
                    if (_current.CurrentLineIndex >= 0 && _current.CurrentLineIndex < _current.Sequence.Lines.Count)
                    {
                        speakerName = ResolveSpeakerName(_current.Sequence.Lines[_current.CurrentLineIndex]);
                    }
                    _eventService.Publish(new DialogueResumedEvent(speakerName));

                    if (_current.IsWaitingForChoice)
                    {
                        // Re-present choices (re-filter in case persistence changed during interruption)
                        CheckForChoicePoint();
                    }
                    else
                    {
                        // Advance past the line the player already heard, then play the next one
                        _current.CurrentLineIndex = GetNextLineIndex(_current.CurrentLineIndex);
                        PlayCurrentLineOrFinish();
                    }
                }
                else
                {
                    // Skip resuming this one and try the next one in stack
                    FinishCurrentDialogue();
                }
            }
            else
            {
                _eventService.Publish(new DialogueEndedEvent());
            }
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        private static string ResolveSpeakerName(DialogueLine line)
        {
            if (line.Character == null) return "Unknown";
            if (!string.IsNullOrEmpty(line.Character.DisplayName)) return line.Character.DisplayName;
            if (!string.IsNullOrEmpty(line.Character.CharacterId)) return line.Character.CharacterId;
            return "Unknown";
        }

        private static bool IsSameSequence(DialogueSequenceSO a, DialogueSequenceSO b, string incomingId)
        {
            if (a == b) return true;
            if (!string.IsNullOrEmpty(incomingId) && !string.IsNullOrEmpty(a.SequenceId))
                return a.SequenceId == incomingId;
            return false;
        }
    }
}
