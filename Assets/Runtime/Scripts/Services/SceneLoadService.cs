using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatRuntime
{
    public class SceneLoadService : ISceneLoadService
    {
        private readonly IEventService _eventService;
        private AsyncOperation _currentLoadOp;

        public float CurrentProgress { get; private set; }
        public bool IsLoading { get; private set; }

        public SceneLoadService(IEventService eventService)
        {
            _eventService = eventService;
        }

        public async void LoadScene(string targetSceneName, string loadingSceneName = "")
        {
            if (IsLoading)
            {
                Debug.LogWarning("SceneLoadService: A load is already in progress!");
                return;
            }

            IsLoading = true;
            CurrentProgress = 0f;

            // 1. Broadcast that loading is starting
            // Listeners (UI, Audio, FadeTransitionHandler) react to this
            _eventService.Publish(new SceneLoadBeginEvent { TargetSceneName = targetSceneName });

            // 2. Wait a moment for systems (Audio/Haptics/UI) to finish their fade out
            await Task.Delay(1000);

            // 3. Force a deep memory cleanup (crucial before loading huge scenes)
            await UnloadAssetsTask();
            GC.Collect();

            // 4. Start loading the target scene in the background
            _currentLoadOp = SceneManager.LoadSceneAsync(targetSceneName, LoadSceneMode.Single);
            _currentLoadOp.allowSceneActivation = false; // Hold it at 90%

            while (_currentLoadOp.progress < 0.9f)
            {
                CurrentProgress = _currentLoadOp.progress;
                _eventService.Publish(new SceneLoadProgressEvent { Progress = CurrentProgress });
                await Task.Yield();
            }

            CurrentProgress = 1f;
            _eventService.Publish(new SceneLoadProgressEvent { Progress = 1f });

            // 5. Brief delay before activating (gives the player a moment in the loading environment)
            await Task.Delay(500);

            AllowSceneActivation(targetSceneName);
        }

        public async void AllowSceneActivation(string loadedSceneName = "")
        {
            if (_currentLoadOp != null)
            {
                _currentLoadOp.allowSceneActivation = true;

                while (!_currentLoadOp.isDone)
                {
                    await Task.Yield();
                }

                _currentLoadOp = null;
                IsLoading = false;

                _eventService.Publish(new SceneLoadCompleteEvent { LoadedSceneName = loadedSceneName });
            }
        }

        private Task LoadSceneTask(string sceneName, LoadSceneMode mode)
        {
            var tcs = new TaskCompletionSource<bool>();
            var op = SceneManager.LoadSceneAsync(sceneName, mode);
            op.completed += _ => tcs.SetResult(true);
            return tcs.Task;
        }

        private Task UnloadAssetsTask()
        {
            var tcs = new TaskCompletionSource<bool>();
            var op = Resources.UnloadUnusedAssets();
            op.completed += _ => tcs.SetResult(true);
            return tcs.Task;
        }
    }
}
