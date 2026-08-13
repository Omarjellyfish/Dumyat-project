using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Presents a <see cref="SceneShowcaseSlideSO"/>: makes one stage live, points the rendering camera
    /// at one authored angle, and draws the lockup over it.
    ///
    /// The chrome here is deliberately thinner than every other slide type. Delta is a dark frosted
    /// panel, but a panel floating in front of the Shipyard hides exactly what the slide exists to
    /// show — so a scene slide gets a scrim, a short accent rule, and type, and nothing else. The
    /// backdrop layer is switched off entirely, which is what lets the 3D render show through the UI.
    /// </summary>
    public sealed class SceneShowcaseRenderer : MonoBehaviour, ISlideRenderer
    {
        [SerializeField] private PresentationView _view;

        [SerializeField, Tooltip("Camera the Cinemachine Brain drives. Left empty, Camera.main is " +
                                 "resolved on each show — never cached, because a stage load can " +
                                 "change which camera is main.")]
        private Camera _camera;

        private IStageRegistry _stages;

        [Inject]
        private void Inject(IStageRegistry stages) => _stages = stages;

        private void Awake()
        {
            if (_view == null) _view = GetComponentInParent<PresentationView>();
            if (_stages == null) _stages = FindFirstObjectByType<StageRegistry>();
            if (_stages == null)
                Debug.LogWarning("[SceneShowcaseRenderer] no StageRegistry — scene slides cannot show.", this);
        }

        public bool CanRender(SlideSO slide) => slide is SceneShowcaseSlideSO;

        public void Hide()
        {
            if (_view == null || !_view.IsReady) return;
            _view.Lockup.AddToClassList("hidden");
            _view.Pips.AddToClassList("hidden");
            _view.Scrim.AddToClassList("hidden");

            // Put every stage to sleep on the way out, so a text slide is never drawn over a live scene.
            (_stages as StageRegistry)?.DeactivateAll();
        }

        public void Show(SlideSO slide, PresentationDeckSO deck)
        {
            if (_view == null || !_view.IsReady) return;
            if (!(slide is SceneShowcaseSlideSO scene))
            {
                Debug.LogWarning($"[SceneShowcaseRenderer] asked to render {slide?.GetType().Name}.", this);
                return;
            }

            SceneStage stage = null;
            int hiddenLayers = 0;
            if (_stages != null)
                stage = _stages.MakeOnlyLive(scene.StageId, scene.AngleId, out hiddenLayers);

            if (stage == null)
                Debug.LogWarning($"[SceneShowcaseRenderer] '{slide.name}' wants stage " +
                                 $"'{scene.StageId}' which is not loaded — the frame will be empty.", this);

            ApplyCullingMask(hiddenLayers);
            DrawLockup(scene, deck, stage);
        }

        /// <summary>
        /// Excludes this angle's hidden layers from the rendering camera. This is how low-poly
        /// placeholders disappear for a close-up without any object being toggled — and why the mask is
        /// reset from a full mask each time rather than accumulated, so a skipped slide cannot leave a
        /// layer hidden for the rest of the deck.
        /// </summary>
        private void ApplyCullingMask(int hiddenLayers)
        {
            Camera cam = _camera != null ? _camera : Camera.main;
            if (cam == null) return;
            cam.cullingMask = ~hiddenLayers;

            // A scene slide is only ever meaningful if the camera draws to the SCREEN. Anything that
            // borrows the camera for an off-screen pass and fails to give it back — the stage prewarmer
            // used to be able to — turns every slide in this section into UI chrome over an empty frame.
            // Cheap to assert, and it names the fault instead of leaving it to be guessed at.
            if (cam.targetTexture != null)
            {
                Debug.LogWarning($"[SceneShowcaseRenderer] camera '{cam.name}' was still rendering to " +
                                 $"'{cam.targetTexture.name}' instead of the screen — restoring it. " +
                                 "Something borrowed the camera and did not put it back.", this);
                cam.targetTexture = null;
            }

            if (!cam.enabled)
            {
                Debug.LogWarning($"[SceneShowcaseRenderer] camera '{cam.name}' was disabled — enabling.", this);
                cam.enabled = true;
            }
        }

        private void DrawLockup(SceneShowcaseSlideSO scene, PresentationDeckSO deck, SceneStage stage)
        {
            _view.Scrim.RemoveFromClassList("hidden");
            _view.Lockup.RemoveFromClassList("hidden");

            _view.LockupPlace.text = scene.PlaceName ?? string.Empty;

            bool hasArabic = !string.IsNullOrEmpty(scene.PlaceNameArabic);
            _view.LockupArabic.EnableInClassList("hidden", !hasArabic);
            if (hasArabic)
            {
                // The string goes in RAW. UI Toolkit's Advanced text generator does the contextual
                // joining and the bidi reordering itself; the only thing needed is to switch it on, which
                // is what the shared ui-arabic class does. Rewriting the text first — shaping it, or
                // reversing it — double-shapes and is exactly the bug this replaced.
                _view.LockupArabic.text = scene.PlaceNameArabic;
                _view.LockupArabic.AddToClassList(UiLanguage.ArabicClass);
            }

            bool hasCaption = !string.IsNullOrEmpty(scene.Caption);
            _view.LockupCaption.EnableInClassList("hidden", !hasCaption);
            if (hasCaption) _view.LockupCaption.text = scene.Caption;

            // NO EYEBROW on a scene slide, for two reasons.
            //
            // The image is the content here, so every pixel of chrome is taken from it — the lockup and
            // the counter already say where we are. And the eyebrow element lives inside the text-slide
            // tree, which the renderer registry hides when it switches away from text: setting its text
            // here only ever produced an invisible label, which is worse than not having one.
            if (deck?.Theme != null)
            {
                _view.LockupRule.style.backgroundColor = deck.Theme.Accent;
                _view.LockupPlace.style.color = deck.Theme.Ink;
                _view.LockupArabic.style.color = deck.Theme.InkMuted;
                _view.LockupCaption.style.color = deck.Theme.InkMuted;
            }

            DrawPips(scene, stage, deck);
        }

        /// <summary>
        /// The "angle 2 of 4" pips. Without them four consecutive slides in one place look identical in
        /// the chrome, and neither the audience nor the presenter can tell whether the deck moved or
        /// the camera did.
        /// </summary>
        private void DrawPips(SceneShowcaseSlideSO scene, SceneStage stage, PresentationDeckSO deck)
        {
            CameraAngleSet set = stage != null ? stage.Angles : null;
            int count = set != null ? set.Count : 0;
            int current = set != null ? set.IndexOf(scene.AngleId) : -1;

            bool show = scene.ShowAnglePips && count > 1 && current >= 0;
            _view.Pips.EnableInClassList("hidden", !show);
            if (!show) return;

            _view.PipsRow.Clear();
            for (int i = 0; i < count; i++)
            {
                var pip = new VisualElement();
                pip.AddToClassList("pip");
                if (i <= current) pip.AddToClassList("on");
                if (deck?.Theme != null && i <= current)
                    pip.style.backgroundColor = deck.Theme.Accent;
                _view.PipsRow.Add(pip);
            }
        }
    }
}
