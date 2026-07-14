using System.Collections;
using System.Collections.Generic;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime.Testing
{
    /// <summary>
    /// In-editor test harness for the Narrative Engine.
    /// Creates all test data at runtime — no manual SO setup needed.
    /// Attach to an empty GameObject in any scene that has DI configured.
    ///
    /// Usage:
    ///   1. Create empty GameObject → Add this component.
    ///   2. Enter Play Mode.
    ///   3. Click the buttons in the Inspector or use the context menu.
    ///   4. Watch the Console for [NarrativeTest] logs showing pass/fail.
    /// </summary>
    public class NarrativeEngineTest : MonoBehaviour
    {
        [Header("Test Control")]
        [SerializeField] private bool _autoRunAllOnStart;
        [SerializeField] private float _delayBetweenTests = 0.5f;

        [Inject] private readonly INarrativeService _narrativeService;
        [Inject] private readonly IEventService _eventService;
        [Inject] private readonly IPlayerPersistenceService _persistenceService;

        // Tracking event fires
        private readonly List<string> _lineLog = new();
        private readonly List<string> _choiceLog = new();
        private int _endedCount;
        private int _interruptedCount;
        private int _resumedCount;
        private int _dismissedCount;
        private string _lastSpeaker;
        private string _lastSubtitle;
        private float _lastDuration;
        private int _lastChoiceCount;

        // Test character + sequences (created at runtime)
        private CharacterDialogueDataSO _charA;
        private CharacterDialogueDataSO _charB;

        private void OnEnable()
        {
            _eventService.Subscribe<DialogueLineStartedEvent>(OnLineStarted);
            _eventService.Subscribe<DialogueChoicesEvent>(OnChoices);
            _eventService.Subscribe<DialogueChoiceDismissedEvent>(OnChoiceDismissed);
            _eventService.Subscribe<DialogueEndedEvent>(OnEnded);
            _eventService.Subscribe<DialogueInterruptedEvent>(OnInterrupted);
            _eventService.Subscribe<DialogueResumedEvent>(OnResumed);
        }

        private void OnDisable()
        {
            _eventService.Unsubscribe<DialogueLineStartedEvent>(OnLineStarted);
            _eventService.Unsubscribe<DialogueChoicesEvent>(OnChoices);
            _eventService.Unsubscribe<DialogueChoiceDismissedEvent>(OnChoiceDismissed);
            _eventService.Unsubscribe<DialogueEndedEvent>(OnEnded);
            _eventService.Unsubscribe<DialogueInterruptedEvent>(OnInterrupted);
            _eventService.Unsubscribe<DialogueResumedEvent>(OnResumed);
        }

        private void Start()
        {
            CreateTestData();

            if (_autoRunAllOnStart)
                StartCoroutine(RunAllTests());
        }

        // =====================================================================
        // Context Menu — run individual tests from Inspector right-click
        // =====================================================================

        [ContextMenu("Run All Tests")]
        public void RunAll() => StartCoroutine(RunAllTests());

        [ContextMenu("Test 1 — Basic Flow")]
        public void RunTest1() => StartCoroutine(Test1_BasicFlow());

        [ContextMenu("Test 2 — Interruption & Resume")]
        public void RunTest2() => StartCoroutine(Test2_InterruptionAndResume());

        [ContextMenu("Test 3 — Priority Queueing")]
        public void RunTest3() => StartCoroutine(Test3_PriorityQueueing());

        [ContextMenu("Test 4 — Persistence Skip")]
        public void RunTest4() => StartCoroutine(Test4_PersistenceSkip());

        [ContextMenu("Test 5 — Persistence Gate Pass")]
        public void RunTest5() => StartCoroutine(Test5_PersistenceGatePass());

        [ContextMenu("Test 6 — Choice Selection")]
        public void RunTest6() => StartCoroutine(Test6_ChoiceSelection());

        [ContextMenu("Test 7 — Interrupt During Choice")]
        public void RunTest7() => StartCoroutine(Test7_InterruptDuringChoice());

        [ContextMenu("Test 8 — Deduplication")]
        public void RunTest8() => StartCoroutine(Test8_Deduplication());

        [ContextMenu("Test 9 — Cancel Current")]
        public void RunTest9() => StartCoroutine(Test9_CancelCurrent());

        [ContextMenu("Test 10 — Non-Interruptible")]
        public void RunTest10() => StartCoroutine(Test10_NonInterruptible());

        [ContextMenu("Test 11 — Stack Overflow Protection")]
        public void RunTest11() => StartCoroutine(Test11_StackOverflowProtection());

        [ContextMenu("Test 12 — Null/Empty Handling")]
        public void RunTest12() => StartCoroutine(Test12_NullHandling());

        // =====================================================================
        // Test Runner
        // =====================================================================

        private IEnumerator RunAllTests()
        {
            Log("========================================");
            Log("NARRATIVE ENGINE TEST SUITE — START");
            Log("========================================");

            yield return Test1_BasicFlow();
            yield return new WaitForSeconds(_delayBetweenTests);

            yield return Test2_InterruptionAndResume();
            yield return new WaitForSeconds(_delayBetweenTests);

            yield return Test3_PriorityQueueing();
            yield return new WaitForSeconds(_delayBetweenTests);

            yield return Test4_PersistenceSkip();
            yield return new WaitForSeconds(_delayBetweenTests);

            yield return Test5_PersistenceGatePass();
            yield return new WaitForSeconds(_delayBetweenTests);

            yield return Test6_ChoiceSelection();
            yield return new WaitForSeconds(_delayBetweenTests);

            yield return Test7_InterruptDuringChoice();
            yield return new WaitForSeconds(_delayBetweenTests);

            yield return Test8_Deduplication();
            yield return new WaitForSeconds(_delayBetweenTests);

            yield return Test9_CancelCurrent();
            yield return new WaitForSeconds(_delayBetweenTests);

            yield return Test10_NonInterruptible();
            yield return new WaitForSeconds(_delayBetweenTests);

            yield return Test11_StackOverflowProtection();
            yield return new WaitForSeconds(_delayBetweenTests);

            yield return Test12_NullHandling();

            Log("========================================");
            Log("NARRATIVE ENGINE TEST SUITE — COMPLETE");
            Log("========================================");
        }

        // =====================================================================
        // Individual Tests
        // =====================================================================

        /// <summary>TEST 1: Basic 3-line dialogue plays in order, fires ended event.</summary>
        private IEnumerator Test1_BasicFlow()
        {
            ResetTracking();
            Log("--- TEST 1: Basic Flow ---");

            var seq = CreateSequence("test_basic", DialoguePriority.Normal,
                Line(_charA, "Hello there!"),
                Line(_charA, "How are you?"),
                Line(_charA, "Goodbye!"));

            _narrativeService.PlayDialogue(seq, seq.Priority);
            yield return null;

            Assert(_narrativeService.IsPlaying, "Should be playing after PlayDialogue");
            Assert(_lineLog.Count == 1, $"First line should auto-fire (got {_lineLog.Count})");
            Assert(_lastSubtitle == "Hello there!", $"First subtitle should be 'Hello there!' (got '{_lastSubtitle}')");
            Assert(_lastSpeaker == "Test Char A", $"Speaker should be 'Test Char A' (got '{_lastSpeaker}')");

            _narrativeService.AdvanceLine();
            Assert(_lastSubtitle == "How are you?", $"Second subtitle should be 'How are you?' (got '{_lastSubtitle}')");

            _narrativeService.AdvanceLine();
            Assert(_lastSubtitle == "Goodbye!", $"Third subtitle should be 'Goodbye!' (got '{_lastSubtitle}')");

            _narrativeService.AdvanceLine();
            Assert(!_narrativeService.IsPlaying, "Should stop playing after last line");
            Assert(_endedCount == 1, $"DialogueEndedEvent should fire once (got {_endedCount})");
            Assert(_lineLog.Count == 3, $"Should have logged 3 lines (got {_lineLog.Count})");

            LogPass("TEST 1");
            yield return null;
        }

        /// <summary>TEST 2: Low-priority dialogue interrupted by high-priority, then resumes.</summary>
        private IEnumerator Test2_InterruptionAndResume()
        {
            ResetTracking();
            Log("--- TEST 2: Interruption & Resume ---");

            var seqLow = CreateSequence("test_low", DialoguePriority.Normal,
                Line(_charA, "Low line 1"),
                Line(_charA, "Low line 2"),
                Line(_charA, "Low line 3"));

            var seqHigh = CreateSequence("test_high", DialoguePriority.High,
                Line(_charB, "URGENT MESSAGE!"));

            _narrativeService.PlayDialogue(seqLow, seqLow.Priority);
            yield return null;

            Assert(_lastSubtitle == "Low line 1", "Low dialogue starts");

            // Interrupt with high priority
            _narrativeService.PlayDialogue(seqHigh, seqHigh.Priority);
            Assert(_interruptedCount == 1, $"Should fire interrupted event (got {_interruptedCount})");
            Assert(_lastSubtitle == "URGENT MESSAGE!", $"High priority line should play (got '{_lastSubtitle}')");

            // Finish the interruption
            _narrativeService.AdvanceLine();
            Assert(_resumedCount == 1, $"Should fire resumed event (got {_resumedCount})");
            // After resume, it should advance past Low line 1 and play Low line 2
            Assert(_lastSubtitle == "Low line 2", $"Should resume to Low line 2 (got '{_lastSubtitle}')");

            // Finish the resumed dialogue
            _narrativeService.AdvanceLine();
            Assert(_lastSubtitle == "Low line 3", "Low line 3 plays");

            _narrativeService.AdvanceLine();
            Assert(!_narrativeService.IsPlaying, "Should be done");
            Assert(_endedCount == 1, "DialogueEndedEvent fires once at the very end");

            LogPass("TEST 2");
            yield return null;
        }

        /// <summary>TEST 3: Equal/lower priority gets queued, not interrupted.</summary>
        private IEnumerator Test3_PriorityQueueing()
        {
            ResetTracking();
            Log("--- TEST 3: Priority Queueing ---");

            var seq1 = CreateSequence("test_q1", DialoguePriority.Normal,
                Line(_charA, "Playing now"));

            var seq2 = CreateSequence("test_q2", DialoguePriority.Low,
                Line(_charB, "Queued low"));

            _narrativeService.PlayDialogue(seq1, seq1.Priority);
            yield return null;

            Assert(_lastSubtitle == "Playing now", "First plays immediately");

            // Try to play lower priority — should queue, not interrupt
            _narrativeService.PlayDialogue(seq2, seq2.Priority);
            Assert(_interruptedCount == 0, "No interruption for lower priority");
            Assert(_lastSubtitle == "Playing now", "Still playing the first one");

            // Finish first dialogue — queued one should play
            _narrativeService.AdvanceLine();
            Assert(_lastSubtitle == "Queued low", $"Queued dialogue plays after first finishes (got '{_lastSubtitle}')");

            _narrativeService.AdvanceLine();
            Assert(!_narrativeService.IsPlaying, "All done");

            LogPass("TEST 3");
            yield return null;
        }

        /// <summary>TEST 4: Line with persistence condition that fails gets skipped.</summary>
        private IEnumerator Test4_PersistenceSkip()
        {
            ResetTracking();
            Log("--- TEST 4: Persistence Skip ---");

            // Line 1 requires key "MetBoss" which player does NOT have
            var line0 = Line(_charA, "Before the gate");
            var line1 = Line(_charA, "You've met the boss!");
            line1.PersistenceConditionKey = "MetBoss";
            line1.RequiredState = true; // requires key to exist
            var line2 = Line(_charA, "After the gate");

            var seq = CreateSequence("test_persist_skip", DialoguePriority.Normal, line0, line1, line2);

            _narrativeService.PlayDialogue(seq, seq.Priority);
            yield return null;

            Assert(_lastSubtitle == "Before the gate", "First line plays");

            _narrativeService.AdvanceLine();
            // Line 1 should be skipped because player doesn't have "MetBoss"
            Assert(_lastSubtitle == "After the gate", $"Line with failed condition should be skipped (got '{_lastSubtitle}')");
            Assert(_lineLog.Count == 2, $"Only 2 lines should have played (got {_lineLog.Count})");

            _narrativeService.AdvanceLine();
            Assert(!_narrativeService.IsPlaying, "Dialogue ends");

            LogPass("TEST 4");
            yield return null;
        }

        /// <summary>TEST 5: Line with persistence condition that passes plays normally.</summary>
        private IEnumerator Test5_PersistenceGatePass()
        {
            ResetTracking();
            Log("--- TEST 5: Persistence Gate Pass ---");

            // Add the key first
            _persistenceService.AddKey("HasSword");

            var line0 = Line(_charA, "Check your gear");
            var line1 = Line(_charA, "You have the sword!");
            line1.PersistenceConditionKey = "HasSword";
            line1.RequiredState = true;

            var seq = CreateSequence("test_persist_pass", DialoguePriority.Normal, line0, line1);

            _narrativeService.PlayDialogue(seq, seq.Priority);
            yield return null;

            Assert(_lastSubtitle == "Check your gear", "First line plays");

            _narrativeService.AdvanceLine();
            Assert(_lastSubtitle == "You have the sword!", $"Gated line should play when key exists (got '{_lastSubtitle}')");

            _narrativeService.AdvanceLine();
            Assert(!_narrativeService.IsPlaying, "Done");

            LogPass("TEST 5");
            yield return null;
        }

        /// <summary>TEST 6: Choice point presents options and jumps to correct line.</summary>
        private IEnumerator Test6_ChoiceSelection()
        {
            ResetTracking();
            Log("--- TEST 6: Choice Selection ---");

            // Lines: 0=prompt, 1=response A, 2=response B, 3=after
            var seq = CreateSequence("test_choice", DialoguePriority.Normal,
                Line(_charA, "What will you do?"),       // 0
                Line(_charA, "You chose to fight!"),     // 1
                Line(_charA, "You chose to flee!"),      // 2
                Line(_charA, "The end."));               // 3

            // Override NextLineIndex for branch lines to jump to ending
            seq.Lines[1].NextLineIndex = 3;
            seq.Lines[2].NextLineIndex = 3;

            // Choice after line 0, options jump to line 1 or 2
            seq.ChoicePoints.Add(new DialogueChoicePoint
            {
                AfterLineIndex = 0,
                PromptText = "Fight or flee?",
                TimeoutSeconds = 0,
                Choices = new List<DialogueChoice>
                {
                    new DialogueChoice { ChoiceText = "Fight!", JumpToLineIndex = 1 },
                    new DialogueChoice { ChoiceText = "Flee!", JumpToLineIndex = 2 }
                }
            });

            _narrativeService.PlayDialogue(seq, seq.Priority);
            yield return null;

            Assert(_lastSubtitle == "What will you do?", "Prompt line plays");
            Assert(_lastChoiceCount == 2, $"Should present 2 choices (got {_lastChoiceCount})");

            // AdvanceLine should be blocked while waiting for choice
            _narrativeService.AdvanceLine();
            Assert(_lastSubtitle == "What will you do?", "AdvanceLine blocked during choice");

            // Select choice 1 (Flee)
            _narrativeService.SelectChoice(1);
            Assert(_dismissedCount == 1, "Choice dismissed event fires");
            Assert(_lastSubtitle == "You chose to flee!", $"Jumped to flee branch (got '{_lastSubtitle}')");

            _narrativeService.AdvanceLine();
            Assert(_lastSubtitle == "The end.", "Jumped to ending via NextLineIndex");

            _narrativeService.AdvanceLine();
            Assert(!_narrativeService.IsPlaying, "Done");

            LogPass("TEST 6");
            yield return null;
        }

        /// <summary>TEST 7: High priority interruption while choices are showing. Choices re-presented on resume.</summary>
        private IEnumerator Test7_InterruptDuringChoice()
        {
            ResetTracking();
            Log("--- TEST 7: Interrupt During Choice ---");

            var seqChoice = CreateSequence("test_choice_int", DialoguePriority.Normal,
                Line(_charA, "Pick one:"),     // 0
                Line(_charA, "You picked A"),  // 1 — branch end
                Line(_charA, "You picked B")); // 2 — branch end

            // Both branch lines should end the dialogue (jump past last index)
            seqChoice.Lines[1].NextLineIndex = 3;
            seqChoice.Lines[2].NextLineIndex = 3;

            seqChoice.ChoicePoints.Add(new DialogueChoicePoint
            {
                AfterLineIndex = 0,
                PromptText = "A or B?",
                Choices = new List<DialogueChoice>
                {
                    new DialogueChoice { ChoiceText = "A", JumpToLineIndex = 1 },
                    new DialogueChoice { ChoiceText = "B", JumpToLineIndex = 2 }
                }
            });

            var seqUrgent = CreateSequence("test_urgent", DialoguePriority.Critical,
                Line(_charB, "EXPLOSION!"));

            _narrativeService.PlayDialogue(seqChoice, seqChoice.Priority);
            yield return null;

            Assert(_lastChoiceCount == 2, "Choices presented");

            // Interrupt while choices are showing
            _narrativeService.PlayDialogue(seqUrgent, seqUrgent.Priority);
            Assert(_interruptedCount == 1, "Interrupted event fires");
            Assert(_lastSubtitle == "EXPLOSION!", "Urgent line plays");

            // Finish urgent dialogue — should resume and re-present choices
            _narrativeService.AdvanceLine();
            Assert(_resumedCount == 1, "Resumed event fires");
            Assert(_lastChoiceCount == 2, $"Choices re-presented after resume (got {_lastChoiceCount})");

            // Now select choice A
            _narrativeService.SelectChoice(0);
            Assert(_lastSubtitle == "You picked A", $"Choice A works after resume (got '{_lastSubtitle}')");

            _narrativeService.AdvanceLine();
            Assert(!_narrativeService.IsPlaying, "Done");

            LogPass("TEST 7");
            yield return null;
        }

        /// <summary>TEST 8: Same sequence cannot play twice simultaneously.</summary>
        private IEnumerator Test8_Deduplication()
        {
            ResetTracking();
            Log("--- TEST 8: Deduplication ---");

            var seq = CreateSequence("test_dedup", DialoguePriority.Normal,
                Line(_charA, "I should only play once"));

            _narrativeService.PlayDialogue(seq, seq.Priority);
            yield return null;

            // Try to play same sequence again
            int linesBefore = _lineLog.Count;
            _narrativeService.PlayDialogue(seq, DialoguePriority.Critical);
            Assert(_lineLog.Count == linesBefore, "Duplicate sequence rejected");
            Assert(_interruptedCount == 0, "No interruption from duplicate");

            _narrativeService.AdvanceLine();
            Assert(!_narrativeService.IsPlaying, "Done normally");

            LogPass("TEST 8");
            yield return null;
        }

        /// <summary>TEST 9: CancelCurrent stops dialogue and moves to next in stack/queue.</summary>
        private IEnumerator Test9_CancelCurrent()
        {
            ResetTracking();
            Log("--- TEST 9: Cancel Current ---");

            var seq1 = CreateSequence("test_cancel1", DialoguePriority.Normal,
                Line(_charA, "Line 1"),
                Line(_charA, "Line 2"),
                Line(_charA, "Line 3"));

            _narrativeService.PlayDialogue(seq1, seq1.Priority);
            yield return null;

            Assert(_lastSubtitle == "Line 1", "Playing");

            _narrativeService.CancelCurrent();
            Assert(!_narrativeService.IsPlaying, "Cancelled — not playing");
            Assert(_endedCount == 1, "Ended event fires after cancel");

            LogPass("TEST 9");
            yield return null;
        }

        /// <summary>TEST 10: Non-interruptible dialogue queues all incoming.</summary>
        private IEnumerator Test10_NonInterruptible()
        {
            ResetTracking();
            Log("--- TEST 10: Non-Interruptible ---");

            var seqLocked = CreateSequence("test_locked", DialoguePriority.Normal,
                Line(_charA, "Cannot interrupt me!"));
            seqLocked.CanBeInterrupted = false;

            var seqCritical = CreateSequence("test_crit", DialoguePriority.Critical,
                Line(_charB, "Even critical gets queued"));

            _narrativeService.PlayDialogue(seqLocked, seqLocked.Priority);
            yield return null;

            Assert(_lastSubtitle == "Cannot interrupt me!", "Locked dialogue plays");

            // Try critical priority — should NOT interrupt
            _narrativeService.PlayDialogue(seqCritical, seqCritical.Priority);
            Assert(_interruptedCount == 0, "No interruption on non-interruptible");
            Assert(_lastSubtitle == "Cannot interrupt me!", "Still playing locked");

            // Finish locked — queued critical should play
            _narrativeService.AdvanceLine();
            Assert(_lastSubtitle == "Even critical gets queued", $"Queued critical plays after locked finishes (got '{_lastSubtitle}')");

            _narrativeService.AdvanceLine();
            Assert(!_narrativeService.IsPlaying, "Done");

            LogPass("TEST 10");
            yield return null;
        }

        /// <summary>TEST 11: Many consecutive persistence-skipped lines don't cause StackOverflow.</summary>
        private IEnumerator Test11_StackOverflowProtection()
        {
            ResetTracking();
            Log("--- TEST 11: Stack Overflow Protection ---");

            // Create a sequence with 50 lines that all require a key the player doesn't have,
            // followed by one valid line
            var lines = new List<DialogueLine>();
            for (int i = 0; i < 50; i++)
            {
                var gatedLine = Line(_charA, $"Gated line {i}");
                gatedLine.PersistenceConditionKey = "NonExistentKey_StackTest";
                gatedLine.RequiredState = true;
                lines.Add(gatedLine);
            }
            lines.Add(Line(_charA, "The survivor line"));

            var seq = CreateSequence("test_stackoverflow", DialoguePriority.Normal, lines.ToArray());

            _narrativeService.PlayDialogue(seq, seq.Priority);
            yield return null;

            // Should have skipped all 50 gated lines and landed on "The survivor line"
            Assert(_lastSubtitle == "The survivor line", $"Should skip to valid line (got '{_lastSubtitle}')");
            Assert(_lineLog.Count == 1, $"Only 1 line should have played (got {_lineLog.Count})");

            _narrativeService.AdvanceLine();
            Assert(!_narrativeService.IsPlaying, "Done");

            LogPass("TEST 11");
            yield return null;
        }

        /// <summary>TEST 12: Null sequence, null subtitles, missing character — no crashes.</summary>
        private IEnumerator Test12_NullHandling()
        {
            ResetTracking();
            Log("--- TEST 12: Null/Empty Handling ---");

            // Null sequence should be silently ignored
            _narrativeService.PlayDialogue(null, DialoguePriority.Normal);
            Assert(!_narrativeService.IsPlaying, "Null sequence ignored");

            // Line with null character
            var lineNoChar = new DialogueLine
            {
                Character = null,
                SubtitleText = "A voice from nowhere"
            };
            var seq = CreateSequence("test_null", DialoguePriority.Normal, lineNoChar);

            _narrativeService.PlayDialogue(seq, seq.Priority);
            yield return null;

            Assert(_lastSpeaker == "Unknown", $"Null character → 'Unknown' speaker (got '{_lastSpeaker}')");
            Assert(_lastSubtitle == "A voice from nowhere", "Subtitle still works");
            Assert(_lastDuration >= 1f, $"Duration should use text fallback (got {_lastDuration})");

            _narrativeService.AdvanceLine();

            // Line with null subtitle text
            var lineNoSub = new DialogueLine
            {
                Character = _charA,
                SubtitleText = null
            };
            var seq2 = CreateSequence("test_null2", DialoguePriority.Normal, lineNoSub);

            _narrativeService.PlayDialogue(seq2, seq2.Priority);
            yield return null;

            Assert(_lastSubtitle == "", "Null subtitle → empty string, no crash");

            _narrativeService.AdvanceLine();
            Assert(!_narrativeService.IsPlaying, "Done");

            LogPass("TEST 12");
            yield return null;
        }

        // =====================================================================
        // Event Handlers
        // =====================================================================

        private void OnLineStarted(DialogueLineStartedEvent evt)
        {
            _lastSpeaker = evt.SpeakerName;
            _lastSubtitle = evt.SubtitleText;
            _lastDuration = evt.Duration;
            _lineLog.Add($"[{evt.SpeakerName}] {evt.SubtitleText}");
            Log($"  LINE → [{evt.SpeakerName}] \"{evt.SubtitleText}\" ({evt.Duration:F1}s)");
        }

        private void OnChoices(DialogueChoicesEvent evt)
        {
            _lastChoiceCount = evt.Choices.Count;
            foreach (var c in evt.Choices)
                _choiceLog.Add(c.ChoiceText);
            Log($"  CHOICES → {evt.Choices.Count} options: {evt.PromptText}");
        }

        private void OnChoiceDismissed(DialogueChoiceDismissedEvent evt)
        {
            _dismissedCount++;
        }

        private void OnEnded(DialogueEndedEvent evt)
        {
            _endedCount++;
            Log("  EVENT → DialogueEnded");
        }

        private void OnInterrupted(DialogueInterruptedEvent evt)
        {
            _interruptedCount++;
            Log("  EVENT → DialogueInterrupted");
        }

        private void OnResumed(DialogueResumedEvent evt)
        {
            _resumedCount++;
            Log($"  EVENT → DialogueResumed (speaker: {evt.SpeakerName})");
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private void ResetTracking()
        {
            _lineLog.Clear();
            _choiceLog.Clear();
            _endedCount = 0;
            _interruptedCount = 0;
            _resumedCount = 0;
            _dismissedCount = 0;
            _lastSpeaker = null;
            _lastSubtitle = null;
            _lastDuration = 0;
            _lastChoiceCount = 0;

            // Make sure nothing is playing from a previous test
            if (_narrativeService.IsPlaying)
                _narrativeService.CancelCurrent();
        }

        private void CreateTestData()
        {
            _charA = ScriptableObject.CreateInstance<CharacterDialogueDataSO>();
            _charA.CharacterId = "CharA";
            _charA.DisplayName = "Test Char A";

            _charB = ScriptableObject.CreateInstance<CharacterDialogueDataSO>();
            _charB.CharacterId = "CharB";
            _charB.DisplayName = "Test Char B";
        }

        private static DialogueLine Line(CharacterDialogueDataSO character, string text)
        {
            return new DialogueLine
            {
                Character = character,
                SubtitleText = text,
                NextLineIndex = -1
            };
        }

        private static DialogueSequenceSO CreateSequence(string id, DialoguePriority priority, params DialogueLine[] lines)
        {
            var seq = ScriptableObject.CreateInstance<DialogueSequenceSO>();
            seq.SequenceId = id;
            seq.Priority = priority;
            seq.CanBeInterrupted = true;
            seq.ResumeAfterInterruption = true;
            seq.Lines = new List<DialogueLine>(lines);
            seq.ChoicePoints = new List<DialogueChoicePoint>();
            return seq;
        }

        private static void Assert(bool condition, string message)
        {
            if (condition)
            {
                Debug.Log($"<color=#4CAF50>[NarrativeTest] ✓ PASS:</color> {message}");
            }
            else
            {
                Debug.LogError($"<color=#F44336>[NarrativeTest] ✗ FAIL:</color> {message}");
            }
        }

        private static void Log(string message)
        {
            Debug.Log($"<color=#2196F3>[NarrativeTest]</color> {message}");
        }

        private static void LogPass(string testName)
        {
            Debug.Log($"<color=#4CAF50>[NarrativeTest] ══ {testName} PASSED ══</color>");
        }
    }
}
