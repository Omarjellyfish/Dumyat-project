using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Turns a counted basket into a HUD card and, sometimes, a voice line. A thin presenter in the same
    /// shape as <see cref="SaleScoreNotifier"/>: it holds no score of its own and decides nothing about
    /// scoring — it only reports what <see cref="HoopScoreTracker"/> already counted.
    ///
    /// Audio goes through <see cref="ISoundService"/> (the pooled audio manager), never a local
    /// AudioSource — so the line takes a pooled source, lands on the voice mixer group, and obeys the
    /// player's volume settings like every other voice line in the game.
    ///
    /// Drop it on any GameObject in a scene that has a ContainerScope. Assign a
    /// <see cref="HoopAnnouncerSO"/>; without one it stays silent rather than inventing wording.
    /// </summary>
    public class HoopScoreAnnouncer : MonoBehaviour
    {
        [Tooltip("Wording, colours and voice-line pools for the basket easter egg.")]
        [SerializeField] private HoopAnnouncerSO _config;

        [Tooltip("Voice track played on a basket. Assign a clip here to use that one line every time; " +
                 "leave it empty to draw at random from the pools on the HoopAnnouncerSO instead. " +
                 "Per-count lines on the SO still win over this. Either way it is played through the " +
                 "audio manager, not a local AudioSource.")]
        [SerializeField] private AudioClip _voiceTrack;

        private INotificationService _notifications;
        private ISoundService _sounds;
        private IEventService _events;

        [Inject]
        private void Inject(INotificationService notifications, ISoundService sounds, IEventService events)
        {
            _notifications = notifications;
            _sounds = sounds;
            _events = events;
        }

        private void OnEnable()
        {
            if (_events == null)
            {
                Debug.LogWarning($"{nameof(HoopScoreAnnouncer)} on '{name}' has no IEventService injected — " +
                                 "baskets will not be announced. Does this scene have a ContainerScope?", this);
                return;
            }
            _events.Subscribe<HoopScoreChangedEvent>(HandleScoreChanged);
        }

        private void OnDisable() => _events?.Unsubscribe<HoopScoreChangedEvent>(HandleScoreChanged);

        // Cached method group so Unsubscribe matches (see CLAUDE.md, Events).
        private void HandleScoreChanged(HoopScoreChangedEvent evt)
        {
            if (_config == null)
            {
                Debug.LogWarning($"{nameof(HoopScoreAnnouncer)} on '{name}': no HoopAnnouncerSO assigned — " +
                                 "nothing to announce with.", this);
                return;
            }

            bool milestone = _config.IsMilestone(evt.Total);
            ShowCard(evt.Total, milestone);
            Speak(evt.Total, milestone);
        }

        private void ShowCard(int total, bool milestone)
        {
            if (_notifications == null) return;

            string title = milestone ? _config.MilestoneTitleFor(total) : _config.Title;
            string kicker = milestone ? _config.MilestoneKicker : _config.Kicker;
            string description = total == 1 ? "First basket." : $"{total} baskets.";

            // A burst of baskets is one run of play, not several results worth reading in turn, so all
            // ordinary cards share a coalesce key and the latest supersedes the queued one — the player
            // only ever wants the current total. Milestones get a unique key so a rare card is never
            // swallowed by the ordinary basket that lands a second later.
            string coalesceKey = milestone ? $"hoop-milestone:{total}" : "hoop-score";

            _notifications.Enqueue(new NotificationData(
                title,
                description,
                _config.Icon,
                _config.Accent,
                -1,                                                             // no score chip
                milestone ? NotificationStyle.Medal : NotificationStyle.Toast,
                kicker,
                coalesceKey));
        }

        private void Speak(int total, bool milestone)
        {
            if (_sounds == null)
            {
                Debug.LogWarning($"{nameof(HoopScoreAnnouncer)} on '{name}': no ISoundService injected — the " +
                                 "voice track cannot play. Does this scene have a ContainerScope?", this);
                return;
            }

            // Most specific wins:
            //   1. a line authored for exactly this basket number,
            //   2. the single track assigned on this component,
            //   3. the SO's random pools, which stay quiet on most ordinary baskets by design.
            AudioClip line = _config.VoiceLineForCount(total);
            if (line == null) line = _voiceTrack;
            if (line == null) line = _config.PickVoiceLine(milestone);
            if (line == null) return;

            // Routed through the audio manager (SoundManager behind ISoundService), so it takes a pooled
            // AudioSource and the voice mixer group rather than an AudioSource sitting on this object.
            _sounds.PlayVoiceLine(line, transform, _config.VoiceVolume);
        }
    }
}
