using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// One voice line pinned to an exact basket number — "on the 5th basket, say this". Authored as a
    /// list on <see cref="HoopAnnouncerSO"/>.
    ///
    /// This is deliberately separate from the random pools: a pool is for lines that are interchangeable,
    /// and this is for a line that only makes sense at one moment ("that's five!"). Drawing that from a
    /// pool would eventually play it at the wrong count.
    /// </summary>
    [System.Serializable]
    public class HoopCountLine
    {
        [Tooltip("Which basket this fires on. 1 is the first basket of the session; the count resets " +
                 "when the scene reloads, because the score is session-only.")]
        [Min(1)]
        [SerializeField] private int _count = 1;

        [Tooltip("Clip played on that exact basket. Left empty, this entry is skipped.")]
        [SerializeField] private AudioClip _clip;

        public int Count => _count;
        public AudioClip Clip => _clip;
    }

    /// <summary>
    /// Look, wording and voice lines for the basket easter egg, so retuning it is an asset edit rather
    /// than a code change. Assigned to <see cref="HoopScoreAnnouncer"/>.
    ///
    /// Voice lines are a POOL, not a script: one is drawn at random, and only some of the time. A line on
    /// every single basket turns a joke into nagging within about a minute of play, which is why
    /// <see cref="OrdinaryVoiceChance"/> defaults well below 1. Milestones always speak — they are rare
    /// enough to stay funny.
    /// </summary>
    [CreateAssetMenu(fileName = "HoopAnnouncer", menuName = "Dumyat/Basketball/Hoop Announcer")]
    public class HoopAnnouncerSO : ScriptableObject
    {
        [Header("Ordinary basket")]
        [Tooltip("Shown as the notification title for a normal basket.")]
        [SerializeField] private string _title = "Basket!";

        [Tooltip("Small uppercase label above the card.")]
        [SerializeField] private string _kicker = "SWISH";

        [Header("Milestones")]
        [Tooltip("Basket counts that get the louder Medal card and always play a line. " +
                 "Order does not matter; duplicates are harmless.")]
        [SerializeField] private int[] _milestones = { 3, 5, 10, 25, 50 };

        [Tooltip("Milestone title. {0} is replaced with the total.")]
        [SerializeField] private string _milestoneTitle = "{0} in a row-ish";

        [Tooltip("Milestone kicker.")]
        [SerializeField] private string _milestoneKicker = "HOT HAND";

        [Header("Presentation")]
        [SerializeField] private Sprite _icon;

        [Tooltip("Delta cyan by default — the accent this project uses for a positive, repeatable action.")]
        [SerializeField] private Color _accent = new Color(0.404f, 0.867f, 0.910f, 1f);

        [Header("Voice lines")]
        [Tooltip("Lines pinned to an exact basket number, e.g. 1 -> \"first one!\", 5 -> \"that's five!\". " +
                 "These BEAT everything else and always play, ignoring the random chance below. Counts " +
                 "with no entry fall through to the pools.")]
        [SerializeField] private HoopCountLine[] _countVoiceLines;

        [Tooltip("Played on an ordinary basket, one drawn at random.")]
        [SerializeField] private AudioClip[] _ordinaryVoiceLines;

        [Tooltip("Played when a milestone is hit, one drawn at random. Falls back to the ordinary pool.")]
        [SerializeField] private AudioClip[] _milestoneVoiceLines;

        [Tooltip("How often an ordinary basket gets a voice line. Milestones ignore this and always speak.")]
        [Range(0f, 1f)]
        [SerializeField] private float _ordinaryVoiceChance = 0.35f;

        [Range(0f, 1f)]
        [SerializeField] private float _voiceVolume = 1f;

        public string Title => _title;
        public string Kicker => _kicker;
        public string MilestoneKicker => _milestoneKicker;
        public Sprite Icon => _icon;
        public Color Accent => _accent;
        public float OrdinaryVoiceChance => _ordinaryVoiceChance;
        public float VoiceVolume => _voiceVolume;

        /// <summary>True when <paramref name="total"/> is one of the configured milestones.</summary>
        public bool IsMilestone(int total)
        {
            if (_milestones == null) return false;
            for (int i = 0; i < _milestones.Length; i++)
                if (_milestones[i] == total) return true;
            return false;
        }

        /// <summary>The milestone title with the total substituted in.</summary>
        public string MilestoneTitleFor(int total) =>
            string.IsNullOrEmpty(_milestoneTitle) ? _title : string.Format(_milestoneTitle, total);

        /// <summary>
        /// The line authored for exactly <paramref name="total"/> baskets, or null when that count has
        /// none. First match wins, so a duplicated count is harmless rather than ambiguous.
        /// </summary>
        public AudioClip VoiceLineForCount(int total)
        {
            if (_countVoiceLines == null) return null;

            for (int i = 0; i < _countVoiceLines.Length; i++)
            {
                HoopCountLine entry = _countVoiceLines[i];
                if (entry != null && entry.Count == total && entry.Clip != null) return entry.Clip;
            }
            return null;
        }

        /// <summary>
        /// A line to play, or null for silence. Milestones prefer their own pool but fall back to the
        /// ordinary one, so a half-filled asset still speaks instead of going quiet at the best moment.
        /// </summary>
        public AudioClip PickVoiceLine(bool milestone)
        {
            if (milestone)
            {
                AudioClip clip = PickFrom(_milestoneVoiceLines);
                return clip != null ? clip : PickFrom(_ordinaryVoiceLines);
            }

            if (Random.value > _ordinaryVoiceChance) return null;
            return PickFrom(_ordinaryVoiceLines);
        }

        private static AudioClip PickFrom(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return null;

            // Null entries are common in a half-filled array in the Inspector; skip them rather than
            // handing a null clip to the sound service.
            int start = Random.Range(0, clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                AudioClip clip = clips[(start + i) % clips.Length];
                if (clip != null) return clip;
            }
            return null;
        }
    }
}
