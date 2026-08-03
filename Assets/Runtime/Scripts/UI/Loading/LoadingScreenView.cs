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

        private VisualElement _root;
        private VisualElement _progressFill;
        private Label _sceneLabel;
        private Label _percentLabel;
        private Label _tipLabel;

        private IVisualElementScheduledItem _tipRotation;
        private IReadOnlyList<string> _tips;
        private int _tipIndex;

        public bool IsVisible { get; private set; }

        private void OnEnable()
        {
            if (_uiDocument == null) _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null) return;

            _root = _uiDocument.rootVisualElement;
            if (_root == null) return;

            _progressFill = _root.Q<VisualElement>("LoadingProgressFill");
            _sceneLabel = _root.Q<Label>("LoadingSceneLabel");
            _percentLabel = _root.Q<Label>("LoadingPercentLabel");
            _tipLabel = _root.Q<Label>("LoadingTipLabel");

            // Nothing on this panel is clickable, and it floats between the player and whatever they
            // were looking at — without this it would swallow every XR ray while visible.
            _root.pickingMode = PickingMode.Ignore;

            SetVisible(IsVisible);
        }

        /// <summary>Shows the panel at 0%, with <paramref name="tips"/> cycling every <paramref name="tipSeconds"/>.</summary>
        public void Show(string destinationName, IReadOnlyList<string> tips, float tipSeconds)
        {
            _tips = tips;
            _tipIndex = 0;

            if (_sceneLabel != null) _sceneLabel.text = destinationName ?? string.Empty;

            SetProgress(0f);
            ShowCurrentTip();
            StartTipRotation(tipSeconds);
            SetVisible(true);
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
