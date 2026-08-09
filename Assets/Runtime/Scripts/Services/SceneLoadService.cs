using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatRuntime
{
    public class SceneLoadService : ISceneLoadService
    {
        /// <summary>
        /// How long the load waits after announcing itself, so the blackout and any audio fade can
        /// finish before the old scene starts being torn down.
        ///
        /// Kept as a constant rather than an injected dependency because <see cref="ScreenFadeService"/>
        /// listens to THIS service's events — depending on it here would be a cycle. Keep it at or
        /// above <see cref="ScreenFadeService.DefaultFadeSeconds"/>.
        /// </summary>
        public const float PreLoadHoldSeconds = 0.45f;

        // AsyncOperation.progress stops here while allowSceneActivation is false; the last 10% is the
        // activation frame, which reports nothing.
        private const float ActivationHoldProgress = 0.9f;

        /// <summary>
        /// Floor on how long the loading screen stays up, measured from the moment it appears.
        ///
        /// It is a floor on the whole transition rather than a tail appended after the scene is ready:
        /// a scene that loads in 4 s has already shown the panel for long enough, and adding a further
        /// pause on top only reads as the load having stalled at 100%.
        /// </summary>
        private const float MinimumVisibleSeconds = 1.5f;

        private readonly IEventService _eventService;
        private readonly IWorldAudioService _worldAudioService;
        private readonly ISoundService _soundService;
        private AsyncOperation _currentLoadOp;

        /// <summary>Load progress, 0 to 1, already remapped off the 0.9 activation ceiling.</summary>
        public float CurrentProgress { get; private set; }
        public bool IsLoading { get; private set; }

        public SceneLoadService(IEventService eventService, IWorldAudioService worldAudioService, ISoundService soundService)
        {
            _eventService = eventService;
            _worldAudioService = worldAudioService;
            _soundService = soundService;
        }

        public async void LoadScene(string targetSceneName, string loadingSceneName = "")
        {
            if (IsLoading)
            {
                Debug.LogWarning("SceneLoadService: A load is already in progress!");
                return;
            }

            if (!string.IsNullOrEmpty(loadingSceneName))
                Debug.LogWarning($"SceneLoadService: a loading scene ('{loadingSceneName}') was requested, " +
                                 "but bridge scenes are not implemented yet — loading directly instead.");

            IsLoading = true;
            SetProgress(0f);

            // async void is the interface's shape, so nothing can await this and nothing would ever
            // see a thrown exception. Without the try/finally a failure would strand IsLoading at
            // true and block every future load for the rest of the session.
            try
            {
                // Announce first: the blinder, the loading overlay and the audio fades all key off
                // this, and the hold below is what gives them time to land.
                _eventService.Publish(new SceneLoadBeginEvent { TargetSceneName = targetSceneName });
                _worldAudioService?.StopAmbient(PreLoadHoldSeconds);

                // The sting is part of the load, not decoration laid over it: the gate below refuses
                // to activate the next scene until it has finished, and the reported progress is
                // capped by how far through the clip we are. Timed from here — it plays across the
                // load rather than after it, so a clip shorter than the load costs nothing.
                float audioSeconds = _soundService?.PlaySceneTransitionSound() ?? 0f;
                float startedAt = Time.realtimeSinceStartup;
                float holdSeconds = Mathf.Max(MinimumVisibleSeconds, audioSeconds);

                await Task.Delay((int)(PreLoadHoldSeconds * 1000f));

                // No Resources.UnloadUnusedAssets()/GC.Collect() here on purpose. Both stall the main
                // thread for hundreds of ms, and they used to run right where the bar was expected to
                // start moving — which is what read as "it freezes, then finishes". Unity already
                // unloads unused assets after a LoadSceneMode.Single load, when the old scene's
                // assets are actually unreferenced and the collection is worth doing.
                _currentLoadOp = SceneManager.LoadSceneAsync(targetSceneName, LoadSceneMode.Single);
                _currentLoadOp.allowSceneActivation = false;

                // One gate for both halves of the load — the scene being ready and the audio being
                // done. The bar is the SLOWER of the two, so it only reaches 100% when the transition
                // is genuinely about to happen; showing scene progress alone would sit full and silent
                // while the sting played out, which reads as a hang.
                //
                // A do/while rather than a while: a scene light enough to already be at 0.9 by the
                // time we get here would otherwise never publish anything, and the bar would jump
                // 0% → 100% with a stall in between. Timed on realtime, since a load started from the
                // pause menu runs with Time.timeScale at 0.
                float progress;
                do
                {
                    float sceneFraction = Mathf.Clamp01(_currentLoadOp.progress / ActivationHoldProgress);
                    float audioFraction = holdSeconds <= 0f
                        ? 1f
                        : Mathf.Clamp01((Time.realtimeSinceStartup - startedAt) / holdSeconds);

                    progress = Mathf.Min(sceneFraction, audioFraction);
                    SetProgress(progress);

                    await Task.Yield();
                }
                while (progress < 1f);

                AllowSceneActivation(targetSceneName);
            }
            catch (Exception e)
            {
                Debug.LogError($"SceneLoadService: load of '{targetSceneName}' failed — {e}");

                _currentLoadOp = null;
                IsLoading = false;

                // Still publish completion, or the blinder and the loading overlay stay up forever
                // and the player is left staring at a black screen with no way out.
                _eventService.Publish(new SceneLoadCompleteEvent { LoadedSceneName = targetSceneName });
            }
        }

        public async void AllowSceneActivation(string loadedSceneName = "")
        {
            if (_currentLoadOp == null) return;

            try
            {
                _currentLoadOp.allowSceneActivation = true;

                // This is the one unavoidable stall: Unity destroys the old scene and wakes every
                // object in the new one in a single blocking frame. It is hidden behind the blinder
                // rather than hidden behind async — nothing can make it not block.
                while (!_currentLoadOp.isDone)
                {
                    await Task.Yield();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"SceneLoadService: activation of '{loadedSceneName}' failed — {e}");
            }
            finally
            {
                _currentLoadOp = null;
                IsLoading = false;

                _eventService.Publish(new SceneLoadCompleteEvent { LoadedSceneName = loadedSceneName });
            }
        }

        private void SetProgress(float normalized)
        {
            CurrentProgress = Mathf.Clamp01(normalized);
            _eventService.Publish(new SceneLoadProgressEvent { Progress = CurrentProgress });
        }
    }
}
