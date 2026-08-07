using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Authoring asset for a single quest. Designers create one of these per quest
    /// via <c>Create ▸ Dumyat ▸ Quests ▸ Quest Definition</c> and populate it in the Inspector.
    /// The <see cref="QuestService"/> reads these at runtime through a <see cref="QuestDatabaseSO"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "NewQuest", menuName = "Dumyat/Quests/Quest Definition", order = 0)]
    public class QuestDefinitionSO : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Unique, stable key used in save data. Must never change once shipped.")]
        public string questID;

        [Tooltip("Player-facing quest name shown in the UI.")]
        public string displayName;

        [Tooltip("Arabic quest name, shown when the player's language setting is Arabic. Leave empty " +
                 "and the UI falls back to displayName, so an untranslated quest still reads.")]
        public string displayNameArabic;

        [TextArea(2, 5)]
        [Tooltip("Player-facing description explaining the objective.")]
        public string description;

        [TextArea(2, 5)]
        [Tooltip("Arabic description. Leave empty to fall back to description.")]
        public string descriptionArabic;

        [Tooltip("The scene this quest belongs to (exact Build Settings name). " +
                 "Quest auto-activates when this scene loads.")]
        public string sceneName;

        [Header("Objective")]
        [Tooltip("Which gameplay event drives this quest's progress.")]
        public QuestTriggerType triggerType;

        [Tooltip("How many times the trigger must fire to complete the quest.")]
        [Min(1)]
        public int targetCount = 1;

        [Header("Target (leave empty for 'any')")]
        [Tooltip("Drag any ScriptableObjects that implement IQuestTarget (ItemData, CraftingRecipe). " +
                 "The service reads TargetID from these to filter events. Leave empty to match any event of the trigger type.")]
        public List<ScriptableObject> targetAssets = new List<ScriptableObject>();

        [Tooltip("Fallback string filter for targets that aren't ScriptableObjects (e.g. fish names). " +
                 "Ignored if targetAssets is set.")]
        public string targetNameFallback;

        [Header("Presentation")]
        [Tooltip("Optional icon shown on the quest's progress toast and completion medal. " +
                 "Leave empty for a plain accent badge.")]
        public Sprite icon;

        [Header("Rewards")]
        [Tooltip("Player-facing reward text.")]
        public string rewardDescription;

        [Tooltip("Arabic reward text. Leave empty to fall back to rewardDescription.")]
        public string rewardDescriptionArabic;

        [Tooltip("Optional EGP to credit on completion. Leave at 0 for no money reward.")]
        [Min(0)]
        public int moneyReward;

        [Tooltip("Optional items to add to the player's inventory on completion.")]
        public List<QuestItemReward> itemRewards = new List<QuestItemReward>();

        [Tooltip("Optional scripted reward to grant on completion, for anything the money/item fields " +
                 "above cannot express (unlocking a blueprint, say). Create via Dumyat > Quests > Rewards.")]
        public QuestRewardSO reward;

        [Header("Persistence")]
        [Tooltip("Optional persistence keys to unlock when this quest completes.")]
        public List<string> persistenceKeysOnComplete = new List<string>();

        // --- Language helpers -------------------------------------------------------
        //
        // Every quest string is authored twice, and every panel that shows one has to make the same
        // "Arabic if there is one, English otherwise" decision. Putting that here means a new panel
        // cannot forget it, and matches how TravelDestination and TutorialStepSO already read.

        /// <summary>Quest name in the player's language, falling back to English.</summary>
        public string DisplayNameFor(bool arabic) => UiLanguage.Pick(arabic, displayName, displayNameArabic);

        /// <summary>Objective text in the player's language, falling back to English.</summary>
        public string DescriptionFor(bool arabic) => UiLanguage.Pick(arabic, description, descriptionArabic);

        /// <summary>Reward text in the player's language, falling back to English.</summary>
        public string RewardDescriptionFor(bool arabic) =>
            UiLanguage.Pick(arabic, rewardDescription, rewardDescriptionArabic);
    }
}
