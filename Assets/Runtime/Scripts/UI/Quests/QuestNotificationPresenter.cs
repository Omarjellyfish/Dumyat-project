using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Turns quest events into HUD notifications: a quest ticking forward raises a small toast card,
    /// and a quest completing raises a medal.
    ///
    /// A thin presenter in the same shape as <see cref="LoadingScreenPresenter"/> — the quest service
    /// stays unaware that a HUD exists, and the HUD stays unaware that quests exist. Root-scoped and
    /// eager, because quests progress in every gameplay scene and nothing ever resolves this by hand.
    /// </summary>
    public sealed class QuestNotificationPresenter : IDisposable
    {
        // Delta palette. Cyan is the accent for ordinary progress; jade reads as "earned" and is kept
        // for the completion medal, so the two never look like the same event at a glance.
        private static readonly Color NewQuestAccent = new Color(0.839f, 0.965f, 0.980f);   // #D6F6FA
        private static readonly Color ProgressAccent = new Color(0.404f, 0.867f, 0.910f);   // #67DDE8
        private static readonly Color CompleteAccent = new Color(0.553f, 0.922f, 0.686f);   // #8DEBAF

        private readonly INotificationService _notifications;
        private readonly IEventService _events;
        private readonly IQuestService _quests;
        private readonly ISettingsService _settings;

        private bool Arabic => UiLanguage.IsArabic(_settings);

        public QuestNotificationPresenter(INotificationService notifications, IEventService events,
                                          IQuestService quests, ISettingsService settings)
        {
            _notifications = notifications;
            _events = events;
            _quests = quests;
            _settings = settings;

            if (_notifications == null)
                Debug.LogWarning($"{nameof(QuestNotificationPresenter)} has no {nameof(INotificationService)} " +
                                 "— quest progress will not be announced.");

            if (_events == null) return;

            _events.Subscribe<QuestActivatedEvent>(HandleActivated);
            _events.Subscribe<QuestProgressedEvent>(HandleProgressed);
            _events.Subscribe<QuestCompletedEvent>(HandleCompleted);
        }

        public void Dispose()
        {
            if (_events == null) return;

            _events.Unsubscribe<QuestActivatedEvent>(HandleActivated);
            _events.Unsubscribe<QuestProgressedEvent>(HandleProgressed);
            _events.Unsubscribe<QuestCompletedEvent>(HandleCompleted);
        }

        // --- Handlers ---------------------------------------------------------------

        private void HandleActivated(QuestActivatedEvent evt)
        {
            QuestDefinitionSO def = Resolve(evt.QuestID);
            if (def == null) return;

            Enqueue(new NotificationData(
                def.DisplayNameFor(Arabic),
                def.DescriptionFor(Arabic),
                def.icon,
                NewQuestAccent,
                -1,
                NotificationStyle.Toast,
                null));
        }

        private void HandleProgressed(QuestProgressedEvent evt)
        {
            // QuestService publishes progress and THEN completion for the tick that finishes a quest,
            // so without this the player gets a toast and a medal back to back for one action — and a
            // one-step quest would announce twice for what is, to them, a single event.
            if (evt.CurrentProgress >= evt.TargetCount) return;

            QuestDefinitionSO def = Resolve(evt.QuestID);
            if (def == null) return;

            string counter = $"{evt.CurrentProgress} / {evt.TargetCount}";

            Enqueue(new NotificationData(
                def.DisplayNameFor(Arabic),
                Arabic ? $"التقدم {counter}" : $"Progress {counter}",
                def.icon,
                ProgressAccent,
                -1,
                NotificationStyle.Toast,
                null));
        }

        private void HandleCompleted(QuestCompletedEvent evt)
        {
            QuestDefinitionSO def = Resolve(evt.QuestID);
            if (def == null) return;

            Enqueue(new NotificationData(
                def.DisplayNameFor(Arabic),
                DescribeReward(def),
                def.icon,
                CompleteAccent,
                -1,
                NotificationStyle.Medal,
                Arabic ? "اكتملت المهمة" : "QUEST COMPLETE"));
        }

        // --- Helpers ----------------------------------------------------------------

        private QuestDefinitionSO Resolve(string questID)
        {
            if (_quests == null) return null;

            QuestDefinitionSO def = _quests.GetDefinition(questID);
            if (def == null)
                Debug.LogWarning($"{nameof(QuestNotificationPresenter)}: quest '{questID}' is not in the " +
                                 "quest database — nothing to announce.");
            return def;
        }

        /// <summary>
        /// One line summarising what the quest just paid out.
        ///
        /// Built from the reward fields rather than only from <c>rewardDescription</c> so a designer
        /// who fills in the money or item lists gets a correct medal without also having to remember
        /// to write the prose. Authored text still wins where it exists, because it can say things the
        /// fields cannot.
        /// </summary>
        private string DescribeReward(QuestDefinitionSO def)
        {
            string authored = def.RewardDescriptionFor(Arabic);
            if (!string.IsNullOrWhiteSpace(authored)) return authored;

            var parts = new List<string>();

            if (def.moneyReward > 0)
                parts.Add(Arabic ? $"{def.moneyReward} جنيه" : $"{def.moneyReward} EGP");

            if (def.itemRewards != null)
            {
                for (int i = 0; i < def.itemRewards.Count; i++)
                {
                    QuestItemReward line = def.itemRewards[i];
                    if (!line.IsValid) continue;
                    parts.Add($"{line.EffectiveQuantity}× {line.item.DisplayName}");
                }
            }

            if (parts.Count == 0) return def.DescriptionFor(Arabic);

            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) sb.Append("  ·  ");
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }

        private void Enqueue(NotificationData data) => _notifications?.Enqueue(data);
    }
}
