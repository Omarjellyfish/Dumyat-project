using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// The loading panel itself: a bar, a percentage and a rotating tip. Deliberately dumb — it has
    /// no services injected and no idea what a scene load is, so <see cref="LoadingScreenPresenter"/>
    /// can drive it and a designer can preview it by calling <see cref="Show"/> from a button.
    ///
    /// This prefab is instantiated once and kept alive across scene loads, so everything here must
    /// survive the scene under it being destroyed.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class LoadingScreenView : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        [Tooltip("Placement solver on this GameObject. Auto-resolved if empty.")]
        [SerializeField] private LazyFollowAnchor _followAnchor;

        private VisualElement _root;
        private VisualElement _progressFill;
        private Label _titleLabel;
        private Label _sceneLabel;
        private Label _statusLabel;
        private Label _percentLabel;
        private Label _tipLabel;

        private IVisualElementScheduledItem _tipRotation;
        private IReadOnlyList<string> _tips;
        private int _tipIndex;

        public bool IsVisible { get; private set; }

        private void OnEnable()
        {
            if (_uiDocument == null) _uiDocument = GetComponent<UIDocument>();
            if (_followAnchor == null) _followAnchor = GetComponent<LazyFollowAnchor>();
            if (_uiDocument == null) return;

            _root = _uiDocument.rootVisualElement;
            if (_root == null) return;

            _progressFill = _root.Q<VisualElement>("LoadingProgressFill");
            _titleLabel = _root.Q<Label>("LoadingTitleLabel");
            _sceneLabel = _root.Q<Label>("LoadingSceneLabel");
            _statusLabel = _root.Q<Label>("LoadingStatusLabel");
            _percentLabel = _root.Q<Label>("LoadingPercentLabel");
            _tipLabel = _root.Q<Label>("LoadingTipLabel");

            // Nothing on this panel is clickable, and it floats between the player and whatever they
            // were looking at — without this it would swallow every XR ray while visible.
            _root.pickingMode = PickingMode.Ignore;

            SetVisible(IsVisible);
        }

        /// <summary>
        /// Shows the panel at 0%, drawing <paramref name="content"/> and cycling its tips every
        /// <paramref name="tipSeconds"/>.
        /// </summary>
        public void Show(in LoadingScreenContent content, float tipSeconds)
        {
            // Start somewhere random in the list rather than always at the first tip.
            //
            // A load lasts a handful of seconds and a tip holds for several, so a player only ever
            // reads the first two or three of them. Always starting at index 0 meant the other
            // twenty-odd tips were, in practice, unreachable — they existed only for anyone who sat
            // through an unusually long load. Cycling onwards from a random start still reads in
            // order; it just does not always begin in the same place.
            _tipIndex = content.Tips != null && content.Tips.Count > 0
                ? Random.Range(0, content.Tips.Count)
                : 0;

            // The GameObject never actually disables between loads — only the root's display flips —
            // so LazyFollowAnchor's OnEnable re-anchor does not fire on the second and later loads.
            // Without this the panel reappears at whatever world yaw the last load left behind, which
            // can be off to the side or behind the player.
            if (_followAnchor != null) _followAnchor.Reanchor();

            ApplyContent(content);

            SetProgress(0f);
            StartTipRotation(tipSeconds);
            SetVisible(true);
        }

        /// <summary>
        /// Redraws every string from <paramref name="content"/> without touching the progress bar or
        /// restarting the tip rotation, so the presenter can re-apply a language change mid-load and
        /// the player does not see the bar jump back or the current tip cut short.
        /// </summary>
        public void ApplyContent(in LoadingScreenContent content)
        {
            _tips = content.Tips;

            // The two lists are the same tips in two languages, so holding the index keeps the player
            // on the same sentence across a language change. Clamped in case they are not equal length.
            if (_tips == null || _tips.Count == 0) _tipIndex = 0;
            else if (_tipIndex >= _tips.Count) _tipIndex = 0;

            if (_titleLabel != null) _titleLabel.text = content.Title ?? string.Empty;
            if (_sceneLabel != null) _sceneLabel.text = content.Destination ?? string.Empty;
            if (_statusLabel != null) _statusLabel.text = content.Status ?? string.Empty;

            ShowCurrentTip();

            // Last, so every label above already carries its final string: ApplyScript walks the tree
            // and switches the Arabic text generator on for whatever it finds.
            UiLanguage.ApplyScript(_root, content.Arabic);
        }

        /// <summary>Sets the bar from a 0..1 value that has ALREADY been remapped by the presenter.</summary>
        public void SetProgress(float normalized)
        {
            float clamped = Mathf.Clamp01(normalized);

            // Width percent rather than a ui:ProgressBar: styling the built-in one's inner elements
            // from USS is fiddly, and a plain filled element matches the Delta hairline look.
            if (_progressFill != null) _progressFill.style.width = Length.Percent(clamped * 100f);
            if (_percentLabel != null) _percentLabel.text = Mathf.RoundToInt(clamped * 100f) + "%";
        }

        public void Hide()
        {
            StopTipRotation();
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            IsVisible = visible;
            if (_root != null) _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void StartTipRotation(float tipSeconds)
        {
            StopTipRotation();

            if (_root == null || _tips == null || _tips.Count <= 1) return;

            // UI Toolkit's scheduler runs on unscaled time, so tips keep cycling even when a load is
            // started from the pause menu with Time.timeScale still at 0.
            _tipRotation = _root.schedule
                .Execute(NextTip)
                .Every(Mathf.RoundToInt(Mathf.Max(0.5f, tipSeconds) * 1000f));
        }

        private void StopTipRotation()
        {
            _tipRotation?.Pause();
            _tipRotation = null;
        }

        private void NextTip()
        {
            if (_tips == null || _tips.Count == 0) return;
            _tipIndex = (_tipIndex + 1) % _tips.Count;
            ShowCurrentTip();
        }

        private void ShowCurrentTip()
        {
            if (_tipLabel == null) return;
            _tipLabel.text = _tips != null && _tips.Count > 0 ? _tips[_tipIndex] : string.Empty;
        }
    }
}
