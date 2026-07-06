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
            // UI should listen to this to fade screen to black
            _eventService.Publish(new SceneLoadBeginEvent { TargetSceneName = targetSceneName });

            // 2. Wait a moment for systems (Audio/Haptics/UI) to finish their fade out
            await Task.Delay(1000);

            // 3. Load the intermediate VR loading environment (e.g. "Car")
            if (!string.IsNullOrEmpty(loadingSceneName))
            {
                // Destroys the old scene instantly and puts player in the car
                await LoadSceneTask(loadingSceneName, LoadSceneMode.Single);
            }

            // 4. Force a deep memory cleanup (crucial before loading huge scenes)
            await UnloadAssetsTask();
            GC.Collect();

            // 5. Start loading the target scene in the background
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

            // Note: In a full game, you might wait for the player to press "Exit Car" here.
            // For now, we will automatically transition after a short delay so you can test it.
            await Task.Delay(2000);

            AllowSceneActivation(targetSceneName);
        }

        public async void AllowSceneActivation(string loadedSceneName = "")
        {
            if (_currentLoadOp != null)
            {
                // This triggers the final transition from the Car to the Target Scene
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
