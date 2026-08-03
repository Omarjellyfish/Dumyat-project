using System;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Blacks the view out around every scene load, by listening to <see cref="SceneLoadBeginEvent"/>
    /// and <see cref="SceneLoadCompleteEvent"/> rather than by being called.
    ///
    /// Driving it from the events is what makes the fade universal: menu loads
    /// (<see cref="GameSessionService"/>), the pause menu and the travel/door path all go through
    /// <see cref="ISceneLoadService"/>, so all three are covered without any of them knowing this
    /// service exists. Previously only the travel path faded, and it faded a screen-space canvas the
    /// headset could not see.
    ///
    /// A POCO for the same reason as <see cref="LoadingScreenPresenter"/>: the blinder it owns is a
    /// DontDestroyOnLoad object living in a scene with no ContainerScope, so it cannot be injected
    /// as a MonoBehaviour.
    /// </summary>
    public sealed class ScreenFadeService : IScreenFadeService, IDisposable
    {
        /// <summary>
        /// Fade length (s). <see cref="SceneLoadService.PreLoadHoldSeconds"/> must stay at or above
        /// this, or the load starts before the view is fully black.
        /// </summary>
        public const float DefaultFadeSeconds = 0.4f;

        private readonly IEventService _eventService;

        private ScreenBlinder _blinder;
        private bool _blinderCreationFailed;

        public float Alpha => _blinder != null ? _blinder.Alpha : 0f;

        public ScreenFadeService(IEventService eventService)
        {
            _eventService = eventService;

            if (_eventService == null)
            {
                Debug.LogWarning($"{nameof(ScreenFadeService)} has no {nameof(IEventService)} — scene " +
                                 "loads will not fade to black.");
                return;
            }

            _eventService.Subscribe<SceneLoadBeginEvent>(HandleLoadBegin);
            _eventService.Subscribe<SceneLoadCompleteEvent>(HandleLoadComplete);
        }

        public void Dispose()
        {
            if (_eventService == null) return;

            _eventService.Unsubscribe<SceneLoadBeginEvent>(HandleLoadBegin);
            _eventService.Unsubscribe<SceneLoadCompleteEvent>(HandleLoadComplete);
        }

        public void FadeToBlack(float seconds = -1f)
        {
            if (!EnsureBlinder()) return;
            _blinder.FadeTo(1f, seconds < 0f ? DefaultFadeSeconds : seconds);
        }

        public void FadeFromBlack(float seconds = -1f)
        {
            if (!EnsureBlinder()) return;
            _blinder.FadeTo(0f, seconds < 0f ? DefaultFadeSeconds : seconds);
        }

        public void SetAlphaImmediate(float alpha)
        {
            if (!EnsureBlinder()) return;
            _blinder.SetAlphaImmediate(alpha);
        }

        private void HandleLoadBegin(SceneLoadBeginEvent evt) => FadeToBlack();

        /// <summary>
        /// Fades back in once the new scene is live.
        ///
        /// The ease only advances on later frames, so anything that repositions the player in its own
        /// <see cref="SceneLoadCompleteEvent"/> handler (<see cref="FadeTransitionHandler"/> moving
        /// them to the arrival point, say) has already finished by the first visible frame —
        /// regardless of which handler the event bus called first.
        /// </summary>
        private void HandleLoadComplete(SceneLoadCompleteEvent evt) => FadeFromBlack();

        private bool EnsureBlinder()
        {
            if (_blinder != null) return true;

            // One attempt only: a missing shader would otherwise re-log on every single load.
            if (_blinderCreationFailed) return false;

            _blinder = ScreenBlinder.Create();
            _blinderCreationFailed = _blinder == null;
            return _blinder != null;
        }
    }
}
