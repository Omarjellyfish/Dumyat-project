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
        /// <summary>
        /// The Unity tag every catchable fish prefab carries, and therefore the string a quest puts
        /// in its target filter to mean "any fish". Kept here rather than read from a prefab because
        /// a sale record has no GameObject left to ask.
        /// </summary>
        private const string FishCategoryTag = "Fish";

        private readonly ISaveService _saveService;
        private readonly IEventService _eventService;
        private readonly QuestDatabaseSO _questDatabase;
        private readonly QuestSaveData _questData;
        private readonly QuestRewardContext _rewardContext;
        private readonly IPlayerPersistenceService _persistenceService;
        private readonly IHapticService _hapticService;

        public QuestService(
            ISaveService saveService,
            IEventService eventService,
            QuestDatabaseSO questDatabase,
            ICraftingService craftingService,
            IInventoryService inventoryService,
            IPlayerPersistenceService persistenceService,
            IWalletService walletService,
            IHapticService hapticService)
        {
            _saveService = saveService;
            _eventService = eventService;
            _questDatabase = questDatabase;
            _questData = _saveService.CurrentSaveData.questData;
            _persistenceService = persistenceService;
            _hapticService = hapticService;

            _rewardContext = new QuestRewardContext
            {
                EventService = eventService,
                CraftingService = craftingService,
                InventoryService = inventoryService,
                WalletService = walletService
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

            // The DEAL, not the ledger row. FishSaleRecordedEvent fires once per fish, so subscribing to it
            // made a four-fish hand-over four separate quest ticks; SellScoreEvent fires once per accepted
            // sale and carries every row, which is what the player actually did.
            _eventService.Subscribe<SellScoreEvent>(OnSaleCompleted);
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
            // The catch carries the Fish itself; its species is the name quests filter on
            // (matches FishSaleRecord.fishType, so catch- and sell-quests target the same string).
            string fishName = evt.Fish != null && evt.Fish.Species != null
                ? evt.Fish.Species.FishType
                : null;

            // The prefab's Unity tag is the CATEGORY. It is read off the GameObject rather than
            // assumed, so a fish whose species asset is missing — Dennis_Fish.prefab ships with none
            // — still counts toward "catch any fish" instead of silently dropping out.
            string tag = evt.Fish != null ? evt.Fish.tag : null;

            AdvanceMatchingQuests(QuestTriggerType.FishCaught, fishName, tag);
        }

        /// <summary>
        /// One closed deal advances a sell-quest by every fish in it that matches the quest's filter, in a
        /// SINGLE step. Selling four fish across the scale is one thing the player did, so it is one tick,
        /// one HUD card, and — when it finishes the quest — one completion.
        /// </summary>
        private void OnSaleCompleted(SellScoreEvent evt)
        {
            IReadOnlyList<FishSaleRecord> records = evt.Records;
            if (records == null || records.Count == 0) return;

            // Iterate a copy — completing a quest modifies the active list.
            var activeSnapshot = new List<string>(_questData.activeQuestIDs);

            for (int i = 0; i < activeSnapshot.Count; i++)
            {
                string questID = activeSnapshot[i];
                QuestDefinitionSO def = _questDatabase.GetByID(questID);
                if (def == null) continue;
                if (def.triggerType != QuestTriggerType.ItemSold) continue;

                int steps = 0;
                for (int r = 0; r < records.Count; r++)
                {
                    FishSaleRecord record = records[r];
                    if (record == null) continue;

                    // A sale record is a row of data with no GameObject left to read a tag from, so the
                    // category is supplied here. A closed deal only ever contains fish, which is what makes
                    // that safe — and it keeps "Fish" meaning the same thing on both sides of the
                    // catch/sell pair. A species-filtered quest ("sell 5 Bouri") still counts only its own.
                    if (IsTargetMatch(def, record.fishType, FishCategoryTag)) steps++;
                }

                Advance(questID, def, steps);
            }

            _saveService.SaveAsync();
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
        /// Whether this event satisfies the quest's target filters.
        ///
        /// A filter matches either the SPECIFIC name the event carries (a fish species, a recipe) or
        /// the CATEGORY tag it belongs to. That second half is what lets one quest cover a whole
        /// family without listing its members: every catchable fish prefab is tagged <c>Fish</c>, so
        /// "Sell 5 Fish" filters on <c>Fish</c> and counts a Bouri, a Denis and a Qaroos alike, while
        /// a quest that really wants one species still filters on <c>Denis</c> and gets only that.
        ///
        /// A filter that matches neither REJECTS the event. That asymmetry is the trap this method
        /// used to spring: setting any filter flips "match anything" to "match only this", so a
        /// filter naming something no event is ever called silently froze the quest at zero.
        /// </summary>
        private static bool IsTargetMatch(QuestDefinitionSO def, string eventName, string eventTag)
        {
            bool hasValidTarget = false;

            if (def.targetAssets != null)
            {
                for (int i = 0; i < def.targetAssets.Count; i++)
                {
                    if (def.targetAssets[i] is IQuestTarget questTarget)
                    {
                        hasValidTarget = true;
                        if (Matches(questTarget.TargetID, eventName, eventTag)) return true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(def.targetNameFallback))
            {
                hasValidTarget = true;
                if (Matches(def.targetNameFallback, eventName, eventTag)) return true;
            }

            // If no filters were set, matches anything
            return !hasValidTarget;
        }

        private static bool Matches(string filter, string eventName, string eventTag) =>
            string.Equals(filter, eventName, System.StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(eventTag) &&
             string.Equals(filter, eventTag, System.StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Scans all active quests for the given <paramref name="triggerType"/>.
        /// If a quest has a target set (SO or string), it must match <paramref name="eventName"/> or
        /// the category <paramref name="eventTag"/>, then advances it by one through <see cref="Advance"/>.
        ///
        /// For triggers where ONE player action can satisfy several steps at once, don't call this in a
        /// loop — count the steps and make a single <see cref="Advance"/> call, the way
        /// <see cref="OnSaleCompleted"/> does for a bundle sale. Ticking N times publishes N
        /// <see cref="QuestProgressedEvent"/>s for what the player experienced as one thing.
        /// </summary>
        private void AdvanceMatchingQuests(QuestTriggerType triggerType, string eventName,
            string eventTag = null)
        {
            // Iterate a copy — completing a quest modifies the active list.
            var activeSnapshot = new List<string>(_questData.activeQuestIDs);

            for (int i = 0; i < activeSnapshot.Count; i++)
            {
                string questID = activeSnapshot[i];
                QuestDefinitionSO def = _questDatabase.GetByID(questID);
                if (def == null) continue;
                if (def.triggerType != triggerType) continue;

                if (!IsTargetMatch(def, eventName, eventTag))
                {
                    continue;
                }

                Advance(questID, def, 1);
            }

            _saveService.SaveAsync();
        }

        /// <summary>
        /// Applies <paramref name="steps"/> of progress to one quest, announces it, and completes the quest
        /// if that reaches the target.
        ///
        /// The single place progress is written, so the one-event-per-thing path and the whole-deal path
        /// can never disagree about what a step costs. <paramref name="steps"/> is why it exists: a bundle
        /// sale has to move the counter by four in one publish, not publish four times.
        /// </summary>
        private void Advance(string questID, QuestDefinitionSO def, int steps)
        {
            if (steps <= 0) return;

            // Clamped so nothing ever has to render "6 / 5" for a player who sold more than the quest asked.
            int progress = Mathf.Min(_questData.GetProgress(questID) + steps, def.targetCount);
            _questData.SetProgress(questID, progress);

            _eventService.Publish(new QuestProgressedEvent(questID, progress, def.targetCount));

            Debug.Log($"[QuestService] '{def.displayName}' progress: {progress}/{def.targetCount}" +
                      (steps > 1 ? $" (+{steps} from one deal)" : string.Empty));

            if (progress >= def.targetCount)
            {
                CompleteQuest(questID, def);
            }
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
            
            _hapticService?.TriggerVibration(0.7f, 0.5f, 0.5f, HapticHand.Both);

            if (def.persistenceKeysOnComplete != null)
            {
                foreach (string key in def.persistenceKeysOnComplete)
                {
                    if (!string.IsNullOrEmpty(key))
                    {
                        _persistenceService.AddKey(key);
                    }
                }
            }

            GrantRewards(def);
        }

        /// <summary>
        /// Pays out everything the definition promises: the inline money and item lines, then the
        /// optional scripted reward asset. All three are independent and optional, so a quest can
        /// hand out cash, goods, both, or nothing.
        /// </summary>
        private void GrantRewards(QuestDefinitionSO def)
        {
            if (def.moneyReward > 0)
            {
                if (_rewardContext.WalletService != null)
                    _rewardContext.WalletService.Add(def.moneyReward, $"Quest reward: {def.displayName}");
                else
                    Debug.LogWarning($"[QuestService] '{def.displayName}' promises {def.moneyReward} EGP but " +
                                     "no wallet service is registered — the money was not paid.");
            }

            if (def.itemRewards != null)
            {
                for (int i = 0; i < def.itemRewards.Count; i++)
                {
                    QuestItemReward line = def.itemRewards[i];
                    if (!line.IsValid) continue;

                    if (_rewardContext.InventoryService == null)
                    {
                        Debug.LogWarning($"[QuestService] '{def.displayName}' promises items but no inventory " +
                                         "service is registered — they were not granted.");
                        break;
                    }

                    _rewardContext.InventoryService.AddItemToInventory(line.item.ItemID, line.EffectiveQuantity);
                }
            }

            if (def.rewards != null)
            {
                for (int i = 0; i < def.rewards.Count; i++)
                {
                    QuestRewardSO reward = def.rewards[i];
                    if (reward != null)
                    {
                        reward.GrantReward(_rewardContext);
                        Debug.Log($"[QuestService] Reward '{reward.name}' granted for '{def.displayName}'.");
                    }
                }
            }
        }
    }
}
