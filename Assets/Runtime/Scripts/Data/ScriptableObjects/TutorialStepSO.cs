using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The way a tutorial step detects completion.
    /// </summary>
    public enum TutorialCompletionType
    {
        /// <summary>Completed when a specific <see cref="IGameEvent"/> fires.</summary>
        Event,
        /// <summary>Completed when the player enters a tagged trigger zone.</summary>
        ZoneEnter,
        /// <summary>Auto-advances after <see cref="TutorialStepSO.DelaySeconds"/>.</summary>
        Delay,
        /// <summary>Completed by an external script calling <c>TutorialRunner.CompleteCurrentStep()</c>.</summary>
        Manual
    }

    /// <summary>
    /// A single instruction card in a tutorial sequence.
    /// Each step defines what to tell the player, where to show the hover UI,
    /// which objects to highlight, and how to detect completion.
    /// Create via: Right-click → Create → Dumyat/Tutorial/Step.
    /// </summary>
    [CreateAssetMenu(fileName = "New Tutorial Step", menuName = "Dumyat/Tutorial/Step")]
    public class TutorialStepSO : ScriptableObject
    {
        [Header("Content")]
        [TextArea(2, 4)]
        [Tooltip("The instruction text shown on the hover UI panel")]
        public string InstructionText;

        [Tooltip("Optional voice line played when this step activates")]
        public DialogueSequenceSO VoiceLine;

        [Tooltip("Optional icon displayed on the hover panel")]
        public Sprite Icon;

        [Header("Completion")]
        public TutorialCompletionType CompletionType;

        [Tooltip("Event completion: full event type name (e.g. 'RecipeSelectedEvent')")]
        public string EventTypeName;

        [Tooltip("ZoneEnter completion: ID of the TutorialAnchor acting as a trigger zone")]
        public string ZoneId;

        [Tooltip("Delay completion: seconds before auto-advancing")]
        public float DelaySeconds;

        [Header("Anchors & Presentation")]
        [Tooltip("Primary anchor ID — the hover UI positions near the FIRST TutorialAnchor found with this ID")]
        public string PrimaryAnchorId;

        [Tooltip("Additional IDs to highlight. ALL TutorialAnchors matching ANY of these IDs get visual attention")]
        public string[] HighlightIds;

        [Tooltip("If true, the TutorialAnchor with PrimaryAnchorId is also treated as a highlight target")]
        public bool HighlightPrimaryAnchor = true;

        [Tooltip("World-space offset from the primary anchor where the hover UI is positioned")]
        public Vector3 AnchorOffset = new Vector3(0f, 0.3f, 0f);

        [Tooltip("Optional haptic pulse when this step activates")]
        public HapticProfileSO HapticOnActivate;

        [Header("Conditions (Optional)")]
        [Tooltip("If set, this step only runs when this persistence key matches RequiredState")]
        public string PersistenceConditionKey;

        [Tooltip("true = key must exist for step to run, false = key must NOT exist")]
        public bool RequiredState = true;
    }
}
