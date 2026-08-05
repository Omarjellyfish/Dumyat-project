using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatRuntime
{
    /// <summary>
    /// Owns what New Game / Continue / Exit mean, and keeps the resume point up to date.
    ///
    /// Registered Eager because it records where the player is: it listens to
    /// <see cref="SceneManager.sceneLoaded"/> and must be live from the very first scene, not from
    /// whenever a menu first asks it something.
    ///
    /// It deliberately listens to <c>sceneLoaded</c> rather than <see cref="SceneLoadCompleteEvent"/>,
    /// which only fires for loads that went through <see cref="ISceneLoadService"/>. A designer
    /// pressing Play directly in a gameplay scene must still update the resume point, or Continue
    /// sends them somewhere they have not been for days.
    /// </summary>
    public sealed class GameSessionService : IGameSessionService, IDisposable
    {
        private readonly ISaveService _save;
        private readonly ISceneLoadService _sceneLoad;
        private readonly IGameStateService _gameState;
        private readonly IEventService _eventService;
        private readonly IFadeTransitionService _fadeTransition;
        private readonly MenuCatalogSO _catalog;

        public GameSessionService(
            ISaveService save,
            ISceneLoadService sceneLoad,
            IGameStateService gameState,
            IEventService eventService,
            IFadeTransitionService fadeTransition,
            MenuCatalogSO catalog)
        {
            _save = save;
            _sceneLoad = sceneLoad;
            _gameState = gameState;
            _eventService = eventService;
            _fadeTransition = fadeTransition;
            _catalog = catalog;

            if (_catalog == null)
                Debug.LogWarning($"{nameof(GameSessionService)} has no {nameof(MenuCatalogSO)} — " +
                                 "New Game, Continue and Exit to Main Menu will do nothing. Assign it " +
                                 "on the GameInstaller (ProjectScope prefab).");

            SceneManager.sceneLoaded += HandleSceneLoaded;

            // Whether the root container is built before or after the first scene finishes loading
            // depends on Reflex's initialisation order, so the first sceneLoaded may already have
            // fired by now. Recording the active scene here covers that; the write is idempotent.
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isLoaded) RecordScene(active.name);
        }

        public void Dispose() => SceneManager.sceneLoaded -= HandleSceneLoaded;

        private SessionSaveData Session => _save?.CurrentSaveData?.sessionData;

        /// <summary>
        /// True when there is somewhere worth going back to.
        ///
        /// The recorded scene is re-checked against the catalog rather than trusted, because a save
        /// outlives the rules that wrote it. A scene that was resumable when the stamp was written
        /// and has since been excluded — or was recorded while the catalog named a DIFFERENT scene
        /// as the main menu — would otherwise resume the player into the menu they are standing in.
        /// Re-asking the current rules lets a stale target heal itself instead of needing the save
        /// wiped by hand.
        /// </summary>
        public bool HasSave
        {
            get
            {
                SessionSaveData session = Session;

                return session != null
                       && session.hasSave
                       && !string.IsNullOrEmpty(session.lastSceneName)
                       && _catalog != null
                       && _catalog.IsResumable(session.lastSceneName);
            }
        }

        public string ResumeSceneName => HasSave ? Session.lastSceneName : string.Empty;
        public string ResumeSavedAtUtc => HasSave ? Session.savedAtUtc : string.Empty;

        public void NewGame()
        {
            if (!CanStartTransition()) return;

            _save.ResetSave();
            _eventService?.Publish(new SaveResetEvent());

            // Leaving Paused before the load matters: Time.timeScale is still 0 if this came from
            // the pause menu, and a scene that starts frozen looks like a hang.
            _gameState?.SetState(GameState.FreeRoaming);

            EnterScene(_catalog.NewGameSceneName);
        }

        public void Continue()
        {
            if (!CanStartTransition()) return;

            if (!HasSave)
            {
                Debug.LogWarning($"{nameof(GameSessionService)}: Continue was called with no save to resume.");
                return;
            }

            _gameState?.SetState(GameState.FreeRoaming);
            EnterScene(ResumeSceneName);
        }

        /// <summary>
        /// Loads a gameplay scene, deliberately WITHOUT carrying the player across.
        ///
        /// Travelling by bus does carry the rig (see <see cref="FadeTransitionHandler"/>), and it is
        /// tempting to reuse that here so scenes with no rig of their own still get one. Doing so is
        /// a trap: <c>LoadSceneMode.Single</c> wakes the new scene before destroying the old one, so
        /// a carried rig briefly coexists with the destination's own rig, and XRI's global XR Origin
        /// lookup lets the destination's interactors bind their runtime anchors to the wrong one.
        /// Whichever rig is disposed of afterwards takes the other's anchors with it.
        ///
        /// <see cref="PlayerRigSpawner"/> covers the rigless scenes instead, spawning after the old
        /// scene is already gone — so there is never a moment with two rigs alive.
        /// </summary>
        private void EnterScene(string sceneName) => _sceneLoad.LoadScene(sceneName);

        public void ExitToMainMenu()
        {
            if (!CanStartTransition()) return;

            _gameState?.SetState(GameState.MainMenu);

            // Fire-and-forget: the load already waits a second for systems to settle, which is far
            // longer than the flush needs, and blocking the button press would stutter the headset.
            _ = _save.FlushAsync();

            _sceneLoad.LoadScene(_catalog.MainMenuSceneName);
        }

        public async void QuitApplication()
        {
            if (_save != null)
            {
                try { await _save.FlushAsync(); }
                catch (Exception e) { Debug.LogError($"{nameof(GameSessionService)}: save flush failed on quit — {e}"); }
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// Guards every entry point. Starting a second load mid-load would leave the first
        /// AsyncOperation orphaned at allowSceneActivation = false, which reads as a hard freeze.
        /// </summary>
        private bool CanStartTransition()
        {
            if (_catalog == null || _sceneLoad == null || _save == null) return false;

            if (_sceneLoad.IsLoading)
            {
                Debug.LogWarning($"{nameof(GameSessionService)}: ignoring a menu action — a scene load is already running.");
                return false;
            }

            // A travel transition can be in flight without ISceneLoadService reporting a load yet:
            // StartTransition reparents the player first. Pressing Continue in that window would leave
            // the handler holding a stale player reference for a scene it is no longer arriving in.
            if (_fadeTransition != null && _fadeTransition.IsTransitioning)
            {
                Debug.LogWarning($"{nameof(GameSessionService)}: ignoring a menu action — a travel transition is already running.");
                return false;
            }

            return true;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive) return;   // an added sub-scene is not a resume point
            RecordScene(scene.name);
        }

        private void RecordScene(string sceneName)
        {
            if (_catalog == null) return;

            _gameState?.SetState(sceneName == _catalog.MainMenuSceneName
                ? GameState.MainMenu
                : GameState.FreeRoaming);

            if (!_catalog.IsResumable(sceneName)) return;

            SessionSaveData session = Session;
            if (session == null) return;

            session.hasSave = true;
            session.lastSceneName = sceneName;
            session.savedAtUtc = DateTime.UtcNow.ToString("o");

            _save.SaveAsync();
        }
    }
}
