using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// How a tutorial step decides the player has DONE the thing it asked for.
    ///
    /// Note the wording: satisfying a step no longer advances it. The trigger unlocks the card's NEXT
    /// button and the player presses it when they are ready — see <c>TutorialRunner.MarkStepSatisfied</c>.
    ///
    /// The ordinals are pinned because <c>CompletionType</c> serialises as an int in 20-odd authored
    /// step assets; reordering these members would silently re-point every one of them.
    /// </summary>
    public enum TutorialCompletionType
    {
        /// <summary>Satisfied when a specific <see cref="IGameEvent"/> fires.</summary>
        Event = 0,
        /// <summary>Satisfied when the player enters a tagged trigger zone.</summary>
        ZoneEnter = 1,
        /// <summary>
        /// Minimum read time: satisfied after <see cref="TutorialStepSO.DelaySeconds"/>. Keep it short
        /// (≤3s) — the player still has to press NEXT afterwards, so a long delay just reads as a
        /// broken button.
        /// </summary>
        Delay = 2,
        /// <summary>
        /// Nothing to do. NEXT is live the moment the card appears. For pure narration, or for an
        /// action the game genuinely cannot detect. (Named <c>Manual</c> until the NEXT gate landed;
        /// the ordinal is unchanged, so no asset needed migrating.)
        /// </summary>
        ReadAndContinue = 3,
        /// <summary>
        /// Satisfied when the step's <see cref="TutorialStepSO.Audio"/> (or <see cref="TutorialStepSO.VoiceLine"/>,
        /// if that's what the runner actually plays for this step) finishes playing. Added after the
        /// others were pinned, so it's appended as 4 rather than inserted in ordinal order — do not
        /// renumber.
        /// </summary>
        AudioFinished = 4
    }

    /// <summary>
    /// What the card's NEXT button does on a given step. Independent of
    /// <see cref="TutorialCompletionType"/>, which says how the step is SATISFIED; this says how the
    /// player gets to leave it.
    /// </summary>
    public enum TutorialNextButton
    {
        /// <summary>
        /// No NEXT button at all — the step ends itself the moment its trigger fires. For a step
        /// whose action IS the transition and where a button would be noise: walking into the
        /// workshop, entering a zone. Needs a real trigger; pair it with ReadAndContinue and the card
        /// flashes past.
        /// </summary>
        Hidden = 0,

        /// <summary>
        /// NEXT is live as soon as the card appears. For narration, and for a tip whose action the
        /// game cannot observe — the player reads it and moves on in their own time.
        /// </summary>
        Always = 1,

        /// <summary>
        /// NEXT is shown but dimmed and unclickable until the step's trigger fires. The default for
        /// any tip that names an action, so the player cannot click past without doing it.
        /// </summary>
        AfterAction = 2
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

        [Tooltip("How the player leaves this step.\n\n" +
                 "AfterAction — NEXT is dimmed until the trigger below fires. The default for any tip " +
                 "that names an action.\n" +
                 "Always — NEXT is live at once. For narration, or an action the game cannot detect.\n" +
                 "Hidden — no NEXT at all; the step ends itself when the trigger fires. For a step " +
                 "whose action IS the transition, like walking into the workshop.\n\n" +
                 "SKIP is separate and always available: the player can abandon the tutorial from any " +
                 "step.")]
        public TutorialNextButton NextButton = TutorialNextButton.AfterAction;

        [Tooltip("Event completion: full event type name (e.g. 'RecipeSelectedEvent')")]
        public string EventTypeName;

        [Tooltip("ZoneEnter completion: ID of the TutorialAnchor acting as a trigger zone")]
        public string ZoneId;

        [Tooltip("Delay completion: seconds of reading before NEXT unlocks")]
        public float DelaySeconds;

        [Tooltip("Safety valve. Seconds after which NEXT unlocks even though this step's trigger never " +
                 "fired, so a player is never trapped on a card they cannot dismiss. 0 uses the " +
                 "runner's default. Set it explicitly on any step whose trigger depends on something " +
                 "that can legitimately go missing — a cast that lands out of bounds, an NPC that " +
                 "never gets approached.")]
        [Min(0f)] public float FallbackUnlockSeconds;

        [Header("Anchors & Presentation")]
        [Tooltip("Primary anchor ID — WHERE THE INSTRUCTION CARD SITS. This is placement only and is " +
                 "NOT highlighted; it is routinely set to Hands or Player, which are not objects the " +
                 "tip is about. To highlight something, name it in Highlight Ids below.")]
        public string PrimaryAnchorId;

        [Tooltip("IDs of the objects THIS TIP TALKS ABOUT. Every TutorialAnchor matching any of these " +
                 "is highlighted. Leave EMPTY for a tip that names no object (a button instruction, or " +
                 "narration) — empty means no highlight, which is the correct look for those steps.")]
        public string[] HighlightIds;

        [Tooltip("Highlight only the ONE anchor nearest the player, rather than every anchor sharing " +
                 "the id.\n\n" +
                 "Leave OFF when any of them will do — 'walk up to a merchant' should light all four " +
                 "stalls. Turn it ON for a tip about the one the player is already at: 'lay the fish " +
                 "on HIS scale' lit all four scales at once in the El Borg market, which points the " +
                 "player at three stalls they are not standing at.\n\n" +
                 "No effect where an id is unique, which is every other scene.")]
        public bool HighlightNearestOnly;

        [Tooltip("World-space offset from the primary anchor where the hover UI is positioned")]
        public Vector3 AnchorOffset = new Vector3(0f, 0.3f, 0f);

        [Tooltip("Optional haptic pulse when this step activates")]
        public HapticProfileSO HapticOnActivate;

        // PersistenceConditionKey / RequiredState used to live here. They were authored on every step
        // asset but never read — TutorialRunner.ActivateStep held a TODO where the evaluation would
        // have gone. Removed rather than implemented: no sequence needs a conditional step yet, and a
        // field that looks like it gates something but doesn't is worse than no field.
    }
}