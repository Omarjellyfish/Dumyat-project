using System;
using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// A branch rule evaluated after a specific step completes.
    /// If the condition is met the Runner jumps to <see cref="JumpToStepIndex"/>;
    /// otherwise it falls through to the next sequential step.
    /// </summary>
    [Serializable]
    public class TutorialBranch
    {
        [Tooltip("Evaluate this branch after the step at this index completes")]
        public int AfterStepIndex;

        [Tooltip("Jump to this step index if the condition is met")]
        public int JumpToStepIndex;

        [Tooltip("Event type name whose payload is checked (e.g. 'CraftingValidatedEvent')")]
        public string ConditionEventName;

        [Tooltip("If true, jump when the event indicates success. If false, jump on failure")]
        public bool JumpOnSuccess = true;
    }

    /// <summary>
    /// An ordered playlist of <see cref="TutorialStepSO"/> steps with optional branching.
    /// Represents one complete tutorial (e.g. "Carpentry Basics").
    /// The <see cref="TutorialRunner"/> walks through the steps sequentially,
    /// evaluating <see cref="Branches"/> after each step to decide whether to
    /// jump or continue linearly.
    ///
    /// Create via: Right-click → Create → Dumyat/Tutorial/Sequence.
    /// </summary>
    [CreateAssetMenu(fileName = "New Tutorial Sequence", menuName = "Dumyat/Tutorial/Sequence")]
    public class TutorialSequenceSO : ScriptableObject
    {
        [Tooltip("Unique ID used for save persistence (e.g. 'tutorial_carpentry')")]
        public string SequenceId;

        [Tooltip("Human-readable name shown in UI (e.g. 'Carpentry Basics')")]
        public string DisplayName;

        [Tooltip("Ordered list of steps — the Runner iterates 0, 1, 2, ... unless a branch redirects")]
        public List<TutorialStepSO> Steps = new();

        [Tooltip("If true, completion is saved so the tutorial won't auto-replay on revisit")]
        public bool PersistCompletion = true;

        [Tooltip("Optional dialogue played when the entire sequence finishes")]
        public DialogueSequenceSO CompletionDialogue;

        [Tooltip("Branch rules — evaluated after a step completes to decide where to jump next")]
        public List<TutorialBranch> Branches = new();
    }
}
