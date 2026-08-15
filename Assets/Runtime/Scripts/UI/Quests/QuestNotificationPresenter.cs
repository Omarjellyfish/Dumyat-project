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

            // The count goes as NUMBERS, not as the words "Progress 4 / 5" — the card draws it as its
            // headline figure and as a row of segments, which the old prose line could not be. That
            // frees the description to say what the player still has to DO.
            //
            // Keyed per quest so a burst of ticks reads as one card at the latest count. Selling four
            // fish in one hand-over advances the quest four times; the player did one thing and wants
            // one answer. Two DIFFERENT quests ticking together still get a card each.
            Enqueue(NotificationData.Progress(
                def.DisplayNameFor(Arabic),
                def.DescriptionFor(Arabic),
                def.icon,
                ProgressAccent,
                evt.CurrentProgress,
                evt.TargetCount,
                ProgressKey(evt.QuestID)));
        }

        private void HandleCompleted(QuestCompletedEvent evt)
        {
            QuestDefinitionSO def = Resolve(evt.QuestID);
            if (def == null) return;

            // Retire the progress card before raising the medal. The tick that finishes a quest is
            // suppressed above, so without this the coalesced card is left holding the count BEFORE the
            // finishing one — selling four fish into a "sell 4" quest sat on "3 / 4" while the medal
            // waited its turn behind it, which reads as the quest having failed to register the sale.
            _notifications?.Dismiss(ProgressKey(evt.QuestID));

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

        /// <summary>
        /// The coalesce key for one quest's progress card. Shared by the card and by the completion that
        /// retires it — two copies of this string would drift apart and the medal would quietly stop
        /// clearing the toast, which is a bug with no symptom until someone finishes a quest.
        /// </summary>
        private static string ProgressKey(string questID) => $"quest-progress:{questID}";

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
