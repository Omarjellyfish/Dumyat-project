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
        // Panel chrome in both languages. The destination name and the tips are content and come
        // from the MenuCatalog, not from here.
        private const string TitleEn = "LOADING";
        private const string TitleAr = "جاري التحميل";
        private const string StatusEn = "PREPARING";
        private const string StatusAr = "بنجهز";

        private readonly IEventService _eventService;
        private readonly ISettingsService _settings;
        private readonly MenuCatalogSO _catalog;
        private readonly LoadingScreenView _viewPrefab;

        private LoadingScreenView _view;

        /// <summary>
        /// The scene the visible overlay is announcing. Kept so a language change mid-load can rebuild
        /// the panel's content — the begin event that carried the name is long gone by then.
        /// </summary>
        private string _targetSceneName;

        public bool IsVisible => _view != null && _view.IsVisible;

        /// <summary>Whether the overlay should draw in Arabic.</summary>
        private bool Arabic => UiLanguage.IsArabic(_settings);

        public LoadingScreenPresenter(IEventService eventService, ISettingsService settings,
                                      MenuCatalogSO catalog, LoadingScreenView viewPrefab)
        {
            _eventService = eventService;
            _settings = settings;
            _catalog = catalog;
            _viewPrefab = viewPrefab;

            if (_viewPrefab == null)
                Debug.LogWarning($"{nameof(LoadingScreenPresenter)} has no view prefab — scene loads will " +
                                 "show nothing. Assign it on the GameInstaller (ProjectScope prefab).");

            if (_settings == null)
                Debug.LogWarning($"{nameof(LoadingScreenPresenter)} has no {nameof(ISettingsService)} — the " +
                                 "loading overlay will stay in English regardless of the Language setting.");

            if (_eventService == null) return;

            _eventService.Subscribe<SceneLoadBeginEvent>(HandleLoadBegin);
            _eventService.Subscribe<SceneLoadProgressEvent>(HandleLoadProgress);
            _eventService.Subscribe<SceneLoadCompleteEvent>(HandleLoadComplete);
            _eventService.Subscribe<LanguageChangedEvent>(HandleLanguageChanged);
        }

        public void Dispose()
        {
            if (_eventService == null) return;

            _eventService.Unsubscribe<SceneLoadBeginEvent>(HandleLoadBegin);
            _eventService.Unsubscribe<SceneLoadProgressEvent>(HandleLoadProgress);
            _eventService.Unsubscribe<SceneLoadCompleteEvent>(HandleLoadComplete);
            _eventService.Unsubscribe<LanguageChangedEvent>(HandleLanguageChanged);
        }

        private void HandleLoadBegin(SceneLoadBeginEvent evt)
        {
            if (!EnsureView()) return;

            _targetSceneName = evt.TargetSceneName;

            _view.Show(BuildContent(), _catalog != null ? _catalog.TipRotationSeconds : 4f);
        }

        /// <summary>
        /// Redraws the overlay in the newly chosen language. Only meaningful while a load is on
        /// screen — the next load rebuilds its content from scratch anyway — but a load started from
        /// the pause menu right after switching language would otherwise show the old language until
        /// it finished.
        /// </summary>
        private void HandleLanguageChanged(LanguageChangedEvent evt)
        {
            if (_view == null || !_view.IsVisible) return;

            _view.ApplyContent(BuildContent());
        }

        /// <summary>Resolves every string the panel shows into the language that is current now.</summary>
        private LoadingScreenContent BuildContent()
        {
            bool ar = Arabic;

            return new LoadingScreenContent(
                ar ? TitleAr : TitleEn,
                _catalog != null ? _catalog.SceneLabelFor(_targetSceneName, ar) : _targetSceneName,
                ar ? StatusAr : StatusEn,
                _catalog != null ? _catalog.LoadingTipsFor(ar) : Array.Empty<string>(),
                ar);
        }

        private void HandleLoadProgress(SceneLoadProgressEvent evt)
        {
            if (_view == null) return;

            // Already a true 0..1 — SceneLoadService remaps AsyncOperation's 0.9 activation ceiling
            // at the source, so every subscriber gets the same normalised value.
            _view.SetProgress(evt.Progress);
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
