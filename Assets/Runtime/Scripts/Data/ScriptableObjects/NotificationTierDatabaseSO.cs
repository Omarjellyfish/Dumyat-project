using System;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Maps a 0-100 selling score to a funny rank notification (title / icon / description / accent colour).
    /// Authored as an asset (see Dumyat/UI/Notification Tiers). <see cref="Resolve"/> picks the first band the
    /// score falls in, so list bands in ASCENDING <see cref="Tier.maxScoreInclusive"/> order.
    /// Mirrors the title+icon+description shape of <c>ItemData</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "NotificationTiers", menuName = "Dumyat/UI/Notification Tiers")]
    public class NotificationTierDatabaseSO : ScriptableObject
    {
        [Serializable]
        public struct Tier
        {
            [Tooltip("This band applies when the score is <= this value. Keep the list ascending.")]
            [Range(0f, 100f)] public float maxScoreInclusive;

            [Tooltip("Rank title, e.g. \"7alangy\". Romanized/Latin renders on the default font.")]
            public string title;

            [TextArea] public string description;

            [Tooltip("Icon sprite for this rank. Leave empty for a plain accent badge until art is ready.")]
            public Sprite icon;

            [Tooltip("Accent colour — drives the title, accent bar, icon badge, medal ring and countdown.")]
            public Color accent;
        }

        [SerializeField] private Tier[] _tiers;

        /// <summary>Resolves a 0-100 score into ready-to-show notification data.</summary>
        public NotificationData Resolve(float score)
        {
            int rounded = Mathf.RoundToInt(Mathf.Clamp(score, 0f, 100f));

            if (_tiers == null || _tiers.Length == 0)
                return new NotificationData("Sold", string.Empty, null, Color.white, rounded);

            Tier chosen = _tiers[_tiers.Length - 1];   // top band is the fallback for 100 / overflow
            for (int i = 0; i < _tiers.Length; i++)
            {
                if (score <= _tiers[i].maxScoreInclusive) { chosen = _tiers[i]; break; }
            }

            return new NotificationData(chosen.title, chosen.description, chosen.icon, chosen.accent, rounded);
        }

        /// <summary>
        /// One ready-to-show notification per configured tier, in list order, each stamped with its band
        /// ceiling as the score. For debug previews (see <c>SaleScoreNotifier</c>'s context menu) or any
        /// "show every rank" UI. Never null.
        /// </summary>
        public NotificationData[] PreviewAll()
        {
            if (_tiers == null || _tiers.Length == 0) return Array.Empty<NotificationData>();

            var previews = new NotificationData[_tiers.Length];
            for (int i = 0; i < _tiers.Length; i++)
            {
                Tier t = _tiers[i];
                int score = Mathf.RoundToInt(Mathf.Clamp(t.maxScoreInclusive, 0f, 100f));
                previews[i] = new NotificationData(t.title, t.description, t.icon, t.accent, score);
            }
            return previews;
        }
    }
}
