using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Plain C# implementation of <see cref="IQuestService"/>, constructed by Reflex.
    /// Subscribes to gameplay events via <see cref="IEventService"/>, tracks progress
    /// against <see cref="QuestDefinitionSO"/> objectives, and persists state through
    /// <see cref="ISaveService"/>. Publishes <see cref="QuestActivatedEvent"/>,
    /// <see cref="QuestProgressedEvent"/>, and <see cref="QuestCompletedEvent"/>.
    ///
    /// Quests are **scene-bound**: each <see cref="QuestDefinitionSO"/> declares a
    /// <c>sceneName</c>. When <see cref="SceneLoadCompleteEvent"/> fires, the service
    /// auto-activates every quest for that scene (skipping already-completed ones).
    /// </summary>
    public sealed class QuestService : IQuestService
    {
        private readonly ISaveService _saveService;
        private readonly IEventService _eventService;
        private readonly QuestDatabaseSO _questDatabase;
        private readonly QuestSaveData _questData;
        private readonly QuestRewardContext _rewardContext;

        public QuestService(
            ISaveService saveService,
            IEventService eventService,
            QuestDatabaseSO questDatabase,
            ICraftingService craftingService,
            IInventoryService inventoryService)
        {
            _saveService = saveService;
            _eventService = eventService;
            _questDatabase = questDatabase;
            _questData = _saveService.CurrentSaveData.questData;

            _rewardContext = new QuestRewardContext
            {
                EventService = eventService,
                CraftingService = craftingService,
                InventoryService = inventoryService
            };

            SubscribeToGameplayEvents();

            Debug.Log($"[QuestService] Initialised — {_questData.activeQuestIDs.Count} active, " +
                      $"{_questData.completedQuestIDs.Count} completed quest(s) loaded from save.");
        }

        // -------------------------------------------------------------------
        // IQuestService — public API
        // -------------------------------------------------------------------

        public void ActivateQuest(string questID)
        {
            if (string.IsNullOrEmpty(questID)) return;

            if (_questData.completedQuestIDs.Contains(questID))
            {
                Debug.Log($"[QuestService] Quest '{questID}' is already completed. Skipping activation.");
                return;
            }

            if (_questData.activeQuestIDs.Contains(questID))
            {
                return; // Silently skip — scene reloads will hit this often.
            }

            QuestDefinitionSO definition = _questDatabase.GetByID(questID);
            if (definition == null)
            {
                Debug.LogWarning($"[QuestService] Cannot activate unknown quest '{questID}'.");
                return;
            }

            _questData.activeQuestIDs.Add(questID);
            _questData.SetProgress(questID, 0);
            _saveService.SaveAsync();

            _eventService.Publish(new QuestActivatedEvent(questID));
            Debug.Log($"[QuestService] Quest activated: '{definition.displayName}' ({questID})");
        }

        public bool IsQuestActive(string questID)
        {
            return _questData.activeQuestIDs.Contains(questID);
        }

        public bool IsQuestCompleted(string questID)
        {
            return _questData.completedQuestIDs.Contains(questID);
        }

        public int GetProgress(string questID)
        {
            return _questData.GetProgress(questID);
        }

        public QuestDefinitionSO GetDefinition(string questID)
        {
            return _questDatabase.GetByID(questID);
        }

        public List<QuestDefinitionSO> GetActiveQuests()
        {
            var results = new List<QuestDefinitionSO>();
            for (int i = 0; i < _questData.activeQuestIDs.Count; i++)
            {
                QuestDefinitionSO def = _questDatabase.GetByID(_questData.activeQuestIDs[i]);
                if (def != null) results.Add(def);
            }
            return results;
        }

        public List<QuestDefinitionSO> GetCompletedQuests()
        {
            var results = new List<QuestDefinitionSO>();
            for (int i = 0; i < _questData.completedQuestIDs.Count; i++)
            {
                QuestDefinitionSO def = _questDatabase.GetByID(_questData.completedQuestIDs[i]);
                if (def != null) results.Add(def);
            }
            return results;
        }

        // -------------------------------------------------------------------
        // Event subscriptions
        // -------------------------------------------------------------------

        private void SubscribeToGameplayEvents()
        {
            // Scene-based auto-activation.
            _eventService.Subscribe<SceneLoadCompleteEvent>(OnSceneLoaded);

            // Gameplay triggers.
            _eventService.Subscribe<CraftingValidatedEvent>(OnCraftingValidated);
            _eventService.Subscribe<FishCaughtEvent>(OnFishCaught);
            _eventService.Subscribe<FishSaleRecordedEvent>(OnFishSaleRecorded);
            _eventService.Subscribe<BlueprintUnlockedEvent>(OnBlueprintUnlocked);
            _eventService.Subscribe<InventoryChangedEvent>(OnInventoryChanged);
        }

        // -------------------------------------------------------------------
        // Scene auto-activation
        // -------------------------------------------------------------------

        /// <summary>
        /// When a scene finishes loading, scans the quest database for all quests
        /// bound to that scene and activates any that aren't already completed.
        /// </summary>
        private void OnSceneLoaded(SceneLoadCompleteEvent evt)
        {
            if (string.IsNullOrEmpty(evt.LoadedSceneName)) return;

            IReadOnlyList<QuestDefinitionSO> allQuests = _questDatabase.AllQuests;
            for (int i = 0; i < allQuests.Count; i++)
            {
                QuestDefinitionSO quest = allQuests[i];
                if (quest == null) continue;

                if (string.Equals(quest.sceneName, evt.LoadedSceneName,
                    System.StringComparison.OrdinalIgnoreCase))
                {
                    ActivateQuest(quest.questID);
                }
            }
        }

        // -------------------------------------------------------------------
        // Event handlers — each checks active quests of the matching trigger type
        // -------------------------------------------------------------------

        private void OnCraftingValidated(CraftingValidatedEvent evt)
        {
            // Only count successful validations.
            if (!evt.Result.IsSuccess) return;

            // ValidationResult is a struct without a recipe name field, so craft-type
            // quests match on trigger type alone (targetName filter is ignored for crafts).
            AdvanceMatchingQuests(QuestTriggerType.CraftValidation, null);
        }

        private void OnFishCaught(FishCaughtEvent evt)
        {
            AdvanceMatchingQuests(QuestTriggerType.FishCaught, evt.FishName);
        }

        private void OnFishSaleRecorded(FishSaleRecordedEvent evt)
        {
            string saleName = evt.Record?.fishType;
            AdvanceMatchingQuests(QuestTriggerType.ItemSold, saleName);
        }

        private void OnBlueprintUnlocked(BlueprintUnlockedEvent evt)
        {
            AdvanceMatchingQuests(QuestTriggerType.BlueprintUnlocked, evt.RecipeName);
        }

        private void OnInventoryChanged(InventoryChangedEvent evt)
        {
            string itemName = evt.Item?.ItemID;
            AdvanceMatchingQuests(QuestTriggerType.ItemCollected, itemName);
        }

        // -------------------------------------------------------------------
        // Core progress logic
        // -------------------------------------------------------------------

        /// <summary>
        /// Resolves the target filter string from a quest definition.
        /// Prefers the SO reference (<see cref="IQuestTarget.TargetID"/>), falls back
        /// to the plain string <c>targetNameFallback</c> for non-SO targets (e.g. fish).
        /// Returns <c>null</c> if no filter is set (matches any event).
        /// </summary>
        private static string ResolveTargetID(QuestDefinitionSO def)
        {
            if (def.targetAsset is IQuestTarget questTarget)
                return questTarget.TargetID;

            if (!string.IsNullOrEmpty(def.targetNameFallback))
                return def.targetNameFallback;

            return null;
        }

        /// <summary>
        /// Scans all active quests for the given <paramref name="triggerType"/>.
        /// If a quest has a target set (SO or string), it must match <paramref name="eventName"/>.
        /// Increments progress by 1, publishes <see cref="QuestProgressedEvent"/>,
        /// and completes the quest when the target is reached.
        /// </summary>
        private void AdvanceMatchingQuests(QuestTriggerType triggerType, string eventName)
        {
            // Iterate a copy — completing a quest modifies the active list.
            var activeSnapshot = new List<string>(_questData.activeQuestIDs);

            for (int i = 0; i < activeSnapshot.Count; i++)
            {
                string questID = activeSnapshot[i];
                QuestDefinitionSO def = _questDatabase.GetByID(questID);
                if (def == null) continue;
                if (def.triggerType != triggerType) continue;

                // Resolve the target filter: SO reference first, then string fallback.
                string targetID = ResolveTargetID(def);

                if (targetID != null &&
                    !string.Equals(targetID, eventName, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int progress = _questData.GetProgress(questID) + 1;
                _questData.SetProgress(questID, progress);

                _eventService.Publish(new QuestProgressedEvent(questID, progress, def.targetCount));

                Debug.Log($"[QuestService] '{def.displayName}' progress: {progress}/{def.targetCount}");

                if (progress >= def.targetCount)
                {
                    CompleteQuest(questID, def);
                }
            }

            _saveService.SaveAsync();
        }

        private void CompleteQuest(string questID, QuestDefinitionSO def)
        {
            _questData.activeQuestIDs.Remove(questID);

            if (!_questData.completedQuestIDs.Contains(questID))
            {
                _questData.completedQuestIDs.Add(questID);
            }

            _eventService.Publish(new QuestCompletedEvent(questID));
            Debug.Log($"[QuestService] Quest COMPLETED: '{def.displayName}' ({questID})");

            // Grant reward if one is assigned.
            if (def.reward != null)
            {
                def.reward.GrantReward(_rewardContext);
                Debug.Log($"[QuestService] Reward granted for '{def.displayName}'.");
            }
        }
    }
}
