using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Draws an <see cref="AudioCompareSlideSO"/>: two lanes, one AudioSource, and a bar visualiser
    /// driven by the clip that is actually sounding.
    ///
    /// ONE SOURCE, NOT TWO. Both lanes share a single AudioSource, which is what makes "only one plays
    /// at a time" structural rather than something the code has to remember to enforce — starting the
    /// other clip necessarily stops the first.
    ///
    /// The bars are eased toward their target rather than snapped to it. Raw per-frame spectrum values
    /// flicker hard enough to look broken on a projector; the smoothing is the difference between a
    /// visualiser that reads as designed and one that reads as noise.
    /// </summary>
    public sealed class AudioCompareRenderer : MonoBehaviour, ISlideRenderer
    {
        /// <summary>Must be a power of two for GetSpectrumData, and 64 is plenty for 48 bars.</summary>
        private const int SpectrumBins = 64;

        [SerializeField] private PresentationView _view;

        [SerializeField, Tooltip("Left empty, one is added to this object at startup. The deck also " +
                                 "needs an AudioListener somewhere or nothing is audible at all.")]
        private AudioSource _source;

        private VisualElement _host;
        private AudioCompareSlideSO _current;

        private readonly List<VisualElement> _beforeBars = new List<VisualElement>();
        private readonly List<VisualElement> _afterBars = new List<VisualElement>();
        private float[] _heights;
        private float[] _spectrum;

        private VisualElement _beforeLane, _afterLane, _beforeFill, _afterFill;
        private Label _beforeTime, _afterTime;

        /// <summary>Which lane is sounding: -1 none, 0 before, 1 after.</summary>
        private int _playing = -1;

        /// <summary>Lanes that have already been heard, so a spent rail can stay full but dimmed.</summary>
        private readonly bool[] _spent = new bool[2];

        private void Awake()
        {
            if (_view == null) _view = GetComponentInParent<PresentationView>();

            if (_source == null) _source = GetComponent<AudioSource>();
            if (_source == null) _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            // 2D: this is a presenter's clip, not a thing in the world.
            _source.spatialBlend = 0f;

            _spectrum = new float[SpectrumBins];
        }

        private bool EnsureHost()
        {
            if (_host != null) return true;
            if (_view == null || !_view.IsReady) return false;

            _host = new VisualElement { name = "audio-host" };
            _host.AddToClassList("audio-host");
            _host.pickingMode = PickingMode.Ignore;
            _view.Content.Add(_host);
            _host.AddToClassList("hidden");
            return true;
        }

        public bool CanRender(SlideSO slide) => slide is AudioCompareSlideSO;

        public void Hide()
        {
            Stop();
            if (_host != null) _host.AddToClassList("hidden");
            if (_view != null && _view.IsReady)
            {
                _view.Scrim.AddToClassList("hidden");
                _view.Lockup.AddToClassList("hidden");
            }
            _current = null;
        }

        public void Show(SlideSO slide, PresentationDeckSO deck)
        {
            if (!EnsureHost()) return;
            if (!(slide is AudioCompareSlideSO audio))
            {
                Debug.LogWarning($"[AudioCompareRenderer] asked to render {slide?.GetType().Name}.", this);
                return;
            }

            Stop();
            _current = audio;
            _spent[0] = _spent[1] = false;

            _view.Scrim.AddToClassList("hidden");
            _view.Lockup.AddToClassList("hidden");

            _host.RemoveFromClassList("hidden");
            _host.Clear();
            _beforeBars.Clear();
            _afterBars.Clear();

            _heights = new float[audio.BarCount * 2];

            PresentationThemeSO theme = deck != null ? deck.Theme : null;

            if (!string.IsNullOrEmpty(audio.Title))
            {
                var title = new Label(audio.Title);
                title.AddToClassList("audio-title");
                if (theme != null) title.style.color = theme.Ink;
                _host.Add(title);
            }

            if (!string.IsNullOrEmpty(audio.Kicker))
            {
                var kicker = new Label(audio.Kicker);
                kicker.AddToClassList("audio-kicker");
                if (theme != null) kicker.style.color = theme.InkMuted;
                _host.Add(kicker);
            }

            _beforeLane = BuildLane(audio, theme, before: true);
            _host.Add(_beforeLane);

            var divider = new VisualElement { pickingMode = PickingMode.Ignore };
            divider.AddToClassList("audio-divider");
            _host.Add(divider);

            _afterLane = BuildLane(audio, theme, before: false);
            _host.Add(_afterLane);

            _host.Add(BuildHint());
        }

        private VisualElement BuildLane(AudioCompareSlideSO audio, PresentationThemeSO theme, bool before)
        {
            var lane = new VisualElement { pickingMode = PickingMode.Ignore };
            lane.AddToClassList("audio-lane");
            if (!before) lane.AddToClassList("audio-lane--after");

            Color tint = before
                ? (theme != null ? theme.Accent : new Color(0.404f, 0.867f, 0.91f))
                : (theme != null ? theme.Positive : new Color(0.553f, 0.922f, 0.686f));
            Color ink = theme != null ? theme.Ink : Color.white;

            // The ring, with the shared speaker symbol inside it.
            var ring = new VisualElement { pickingMode = PickingMode.Ignore };
            ring.AddToClassList("audio-icon");
            ring.style.borderTopColor = tint;
            ring.style.borderBottomColor = tint;
            ring.style.borderLeftColor = tint;
            ring.style.borderRightColor = tint;

            VisualElement speaker = DiagramIcons.Build(DiagramIcon.Speaker, 92f, ink, tint);
            speaker.style.left = 60f;
            speaker.style.top = 60f;
            ring.Add(speaker);
            lane.Add(ring);

            var body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.AddToClassList("audio-body");

            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("audio-head");

            var tag = new Label(before ? audio.BeforeLabel : audio.AfterLabel);
            tag.AddToClassList("audio-tag");
            tag.style.color = before ? new Color(0.84f, 0.965f, 0.98f, 0.32f) : tint;
            head.Add(tag);

            AudioClip clip = before ? audio.BeforeClip : audio.AfterClip;
            var time = new Label("0:00 / " + Format(clip != null ? clip.length : 0f));
            time.AddToClassList("audio-time");
            head.Add(time);
            if (before) _beforeTime = time; else _afterTime = time;

            body.Add(head);

            var viz = new VisualElement { pickingMode = PickingMode.Ignore };
            viz.AddToClassList("audio-viz");
            List<VisualElement> bars = before ? _beforeBars : _afterBars;
            for (int i = 0; i < audio.BarCount; i++)
            {
                var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                bar.AddToClassList("audio-bar");
                bar.style.height = new Length(7f, LengthUnit.Percent);
                viz.Add(bar);
                bars.Add(bar);
            }
            body.Add(viz);

            var rail = new VisualElement { pickingMode = PickingMode.Ignore };
            rail.AddToClassList("audio-rail");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("audio-rail-fill");
            fill.style.backgroundColor = tint;
            fill.style.width = new Length(0f, LengthUnit.Percent);
            rail.Add(fill);
            body.Add(rail);
            if (before) _beforeFill = fill; else _afterFill = fill;

            lane.Add(body);
            return lane;
        }

        private VisualElement BuildHint()
        {
            var hint = new VisualElement { pickingMode = PickingMode.Ignore };
            hint.AddToClassList("audio-hint");
            foreach (string k in new[] { "Z", "X" })
            {
                var key = new Label(k);
                key.AddToClassList("audio-key");
                hint.Add(key);
            }
            return hint;
        }

        // ---- playback ---------------------------------------------------------------------------

        /// <summary>Bound to Z. Starting either lane necessarily stops the other — one shared source.</summary>
        public void PlayBefore() => Play(0);

        /// <summary>Bound to X.</summary>
        public void PlayAfter() => Play(1);

        private void Play(int lane)
        {
            if (_current == null || _source == null) return;

            AudioClip clip = lane == 0 ? _current.BeforeClip : _current.AfterClip;
            if (clip == null)
            {
                Debug.LogWarning($"[AudioCompareRenderer] '{_current.name}' has no " +
                                 (lane == 0 ? "before" : "after") + " clip.", _current);
                return;
            }

            // Pressing the live lane again restarts it, which is what a presenter re-playing a clip
            // actually wants; pressing the other swaps.
            if (_playing >= 0) _spent[_playing] = true;

            _source.Stop();
            _source.clip = clip;
            _source.time = 0f;
            _source.Play();
            _playing = lane;
            _spent[lane] = false;
        }

        private void Stop()
        {
            if (_source != null && _source.isPlaying) _source.Stop();
            _playing = -1;
        }

        private void Update()
        {
            if (_current == null || _host == null || _host.ClassListContains("hidden")) return;

            bool sounding = _source != null && _source.isPlaying;
            if (_playing >= 0 && !sounding)
            {
                // Ran to the end on its own.
                _spent[_playing] = true;
                _playing = -1;
            }

            if (sounding && _playing >= 0) _source.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);

            UpdateLane(0, _beforeBars, _beforeFill, _beforeTime, sounding);
            UpdateLane(1, _afterBars, _afterFill, _afterTime, sounding);
        }

        private void UpdateLane(int lane, List<VisualElement> bars, VisualElement fill, Label time,
                                bool sounding)
        {
            if (bars.Count == 0) return;

            bool live = sounding && _playing == lane;
            int offset = lane * _current.BarCount;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float ease = 1f - Mathf.Exp(-_current.Smoothing * dt);

            for (int i = 0; i < bars.Count; i++)
            {
                float target = 7f;

                if (live)
                {
                    // Bars map across the spectrum's lower half, where speech actually lives — the top
                    // bins are almost silent for a voice and would leave a third of the lane dead.
                    int bin = Mathf.Clamp(Mathf.RoundToInt(i / (float)bars.Count * (SpectrumBins * 0.5f)),
                                          0, SpectrumBins - 1);
                    float v = Mathf.Sqrt(_spectrum[bin]) * _current.Gain;
                    target = Mathf.Clamp(7f + v * 100f, 7f, 96f);
                }

                int h = offset + i;
                _heights[h] = Mathf.Lerp(_heights[h], target, ease);
                bars[i].style.height = new Length(_heights[h], LengthUnit.Percent);
                bars[i].EnableInClassList("audio-bar--live", live);
            }

            if (fill != null)
            {
                float t = live && _source.clip != null && _source.clip.length > 0.01f
                    ? Mathf.Clamp01(_source.time / _source.clip.length)
                    : (_spent[lane] ? 1f : 0f);
                fill.style.width = new Length(t * 100f, LengthUnit.Percent);
                // A spent rail stays full but steps back, so the eye knows which one has been heard.
                fill.style.opacity = live ? 1f : (_spent[lane] ? 0.35f : 1f);
            }

            if (time != null && _current != null)
            {
                AudioClip clip = lane == 0 ? _current.BeforeClip : _current.AfterClip;
                float length = clip != null ? clip.length : 0f;
                float at = live ? _source.time : (_spent[lane] ? length : 0f);
                time.text = Format(at) + " / " + Format(length);
            }
        }

        private static string Format(float seconds)
        {
            if (seconds < 0f || float.IsNaN(seconds)) return "0:00";
            int total = Mathf.FloorToInt(seconds);
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        private void OnDisable() => Stop();
    }
}
