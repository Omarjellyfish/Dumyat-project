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

        [TextArea(2, 4)]
        [Tooltip("Egyptian-Arabic version of the instruction, shown when the player's subtitle " +
                 "setting is Arabic. Leave empty and the card falls back to InstructionText, so a " +
                 "step without a translation still reads rather than going blank.")]
        public string InstructionTextArabic;

        [Tooltip("Optional audio for the tutorial step")]
        public AudioClip Audio;

        [Tooltip("Optional voice line played when this step activates")]
        public DialogueSequenceSO VoiceLine;

        [Tooltip("Optional icon displayed on the hover panel")]
        public Sprite Icon;

        [Tooltip("Optional video played on the hover panel (overrides Icon if set)")]
        public UnityEngine.Video.VideoClip Video;

        [Header("Completion")]
        public TutorialCompletionType CompletionType;

        [Tooltip("Uncheck to make this step MANDATORY: the Skip and Next buttons are hidden and the " +
                 "step can only end by its own completion trigger firing. Use it for a step the rest " +
                 "of the tutorial makes no sense without — walking into the workshop, say, when every " +
                 "later step points at a tool inside it.")]
        public bool Skippable = true;

        [Tooltip("Event completion: full event type name (e.g. 'RecipeSelectedEvent')")]
        public string EventTypeName;

        [Tooltip("ZoneEnter completion: ID of the TutorialAnchor acting as a trigger zone")]
        public string ZoneId;

        [Tooltip("Delay completion: seconds before auto-advancing")]
        public float DelaySeconds;

        [Header("Anchors & Presentation")]
        [Tooltip("Primary anchor ID — WHERE THE INSTRUCTION CARD SITS. This is placement only and is " +
                 "NOT highlighted; it is routinely set to Hands or Player, which are not objects the " +
                 "tip is about. To highlight something, name it in Highlight Ids below.")]
        public string PrimaryAnchorId;

        [Tooltip("IDs of the objects THIS TIP TALKS ABOUT. Every TutorialAnchor matching any of these " +
                 "is highlighted. Leave EMPTY for a tip that names no object (a button instruction, or " +
                 "narration) — empty means no highlight, which is the correct look for those steps.")]
        public string[] HighlightIds;

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
