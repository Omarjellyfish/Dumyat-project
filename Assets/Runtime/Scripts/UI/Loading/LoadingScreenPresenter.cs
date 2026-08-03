using System;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Shows the loading overlay for the duration of a scene load.
    ///
    /// The overlay is a persistent DontDestroyOnLoad object rather than something living in a scene,
    /// because <see cref="SceneLoadService"/> loads with <c>LoadSceneMode.Single</c>: any loading UI
    /// belonging to either the old or the new scene would be destroyed partway through and could
    /// never show its own completion. <see cref="LazyFollowAnchor"/> on the prefab re-acquires
    /// <c>Camera.main</c> when the old scene's camera dies, so the panel re-anchors in front of the
    /// player on the other side without anything here having to know a swap happened.
    ///
    /// A POCO rather than a MonoBehaviour: an object marked DontDestroyOnLoad lives in a scene with
    /// no ContainerScope, so Reflex's self-injector cannot resolve services for it. Owning the view
    /// from a constructor-injected service sidesteps that entirely.
    /// </summary>
    public sealed class LoadingScreenPresenter : ILoadingScreenService, IDisposable
    {
        private readonly IEventService _eventService;
        private readonly MenuCatalogSO _catalog;
        private readonly LoadingScreenView _viewPrefab;

        private LoadingScreenView _view;

        public bool IsVisible => _view != null && _view.IsVisible;

        public LoadingScreenPresenter(IEventService eventService, MenuCatalogSO catalog, LoadingScreenView viewPrefab)
        {
            _eventService = eventService;
            _catalog = catalog;
            _viewPrefab = viewPrefab;

            if (_viewPrefab == null)
                Debug.LogWarning($"{nameof(LoadingScreenPresenter)} has no view prefab — scene loads will " +
                                 "show nothing. Assign it on the GameInstaller (ProjectScope prefab).");

            if (_eventService == null) return;

            _eventService.Subscribe<SceneLoadBeginEvent>(HandleLoadBegin);
            _eventService.Subscribe<SceneLoadProgressEvent>(HandleLoadProgress);
            _eventService.Subscribe<SceneLoadCompleteEvent>(HandleLoadComplete);
        }

        public void Dispose()
        {
            if (_eventService == null) return;

            _eventService.Unsubscribe<SceneLoadBeginEvent>(HandleLoadBegin);
            _eventService.Unsubscribe<SceneLoadProgressEvent>(HandleLoadProgress);
            _eventService.Unsubscribe<SceneLoadCompleteEvent>(HandleLoadComplete);
        }

        private void HandleLoadBegin(SceneLoadBeginEvent evt)
        {
            if (!EnsureView()) return;

            _view.Show(
                evt.TargetSceneName,
                _catalog != null ? _catalog.LoadingTips : Array.Empty<string>(),
                _catalog != null ? _catalog.TipRotationSeconds : 4f);
        }

        private void HandleLoadProgress(SceneLoadProgressEvent evt)
        {
            if (_view == null) return;

            // SceneLoadService holds activation at allowSceneActivation = false, so AsyncOperation.progress
            // tops out at 0.9 and its loop exits there. Bound raw, the bar would stall just short of full
            // for the whole activation step and read as a hang — so 0.9 is remapped to 100%.
            _view.SetProgress(Mathf.Clamp01(evt.Progress / 0.9f));
        }

        private void HandleLoadComplete(SceneLoadCompleteEvent evt)
        {
            if (_view == null) return;

            // Hidden, not destroyed: the next load reuses it, which costs one dormant UIDocument and
            // avoids re-instantiating a panel during the frame the new scene is settling.
            _view.Hide();
        }

        private bool EnsureView()
        {
            if (_view != null) return true;
            if (_viewPrefab == null) return false;

            _view = UnityEngine.Object.Instantiate(_viewPrefab);
            UnityEngine.Object.DontDestroyOnLoad(_view.gameObject);
            return true;
        }
    }
}
